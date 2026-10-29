using System.Globalization;
using System.Text;
using CampusFlow.Chapel;
using Npgsql;
using NpgsqlTypes;

internal static class ChapelAttendanceProcessor
{
    private const int MinimumRoster = 500;
    private const int MaximumRoster = 2000;
    private const int MinimumMatchedScans = 300;

    internal static async Task RunAsync(NpgsqlConnection db, Guid tenantId, string sha256,
        byte[] content, DateTime nowCentral, DateOnly firstDate, bool apply)
    {
        var schedule = LoadSchedule();
        using var csv = new StreamReader(new MemoryStream(content), new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        var scans = ChapelScanCsvReader.ReadRows(csv).ToArray();
        var lastScanDate = DateOnly.FromDateTime(scans.Max(x => x.ScannedAtLocal));
        await using (var unscheduled = new NpgsqlCommand("""
            SELECT DISTINCT "TermCalendarId", "ChapelAtLocal"
            FROM "AppChapelAttendanceRecords"
            WHERE "TenantId" = @tenant AND NOT "IsDeleted"
              AND "Status" = 0 AND "Source" IN (0, 3)
              AND "ChapelAtLocal" >= @first AND "ChapelAtLocal" < @finished
              AND "ChapelAtLocal"::date <= @last
            """, db))
        {
            unscheduled.Parameters.AddWithValue("tenant", tenantId);
            unscheduled.Parameters.AddWithValue("first", firstDate.ToDateTime(TimeOnly.MinValue));
            unscheduled.Parameters.AddWithValue("finished", nowCentral.AddHours(-2));
            unscheduled.Parameters.AddWithValue("last", lastScanDate.ToDateTime(TimeOnly.MinValue));
            await using var missing = await unscheduled.ExecuteReaderAsync();
            while (await missing.ReadAsync())
            {
                var term = missing.GetInt32(0);
                var at = missing.GetDateTime(1);
                if (!schedule.Any(x => x.TermCalendarId == term && x.Window.StartLocal == at))
                    throw new InvalidOperationException($"A pending Chapel event ({term}, {at:yyyy-MM-dd HH:mm}) has no configured schedule. Attendance was not changed.");
            }
        }
        var events = schedule.Where(x => DateOnly.FromDateTime(x.Window.StartLocal) >= firstDate &&
                                         DateOnly.FromDateTime(x.Window.StartLocal) <= lastScanDate &&
                                         x.Window.EndLocal.AddHours(1) < nowCentral)
            .OrderBy(x => x.Window.StartLocal).ToArray();

        foreach (var scheduled in events)
        {
            await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(20261028, @term)", db, tx))
                {
                    guard.Parameters.AddWithValue("term", scheduled.TermCalendarId);
                    await guard.ExecuteNonQueryAsync();
                }

                var rows = new List<AttendanceRow>();
                await using (var command = new NpgsqlCommand("""
                    SELECT "Id", "StudentId", "Status", "Source"
                    FROM "AppChapelAttendanceRecords"
                    WHERE "TenantId" = @tenant AND "TermCalendarId" = @term
                      AND "ChapelAtLocal" = @at AND NOT "IsDeleted"
                    FOR UPDATE
                    """, db, tx))
                {
                    command.Parameters.AddWithValue("tenant", tenantId);
                    command.Parameters.AddWithValue("term", scheduled.TermCalendarId);
                    command.Parameters.AddWithValue("at", scheduled.Window.StartLocal);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        rows.Add(new AttendanceRow(reader.GetGuid(0), reader.GetString(1),
                            reader.GetInt32(2), reader.GetInt32(3)));
                }

                if (rows.Count < MinimumRoster || rows.Count > MaximumRoster ||
                    rows.Select(x => x.StudentId).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
                    rows.Any(x => string.IsNullOrWhiteSpace(x.StudentId)))
                {
                    Console.WriteLine($"CHAPEL {scheduled.Window.StartLocal:yyyy-MM-dd HH:mm}: skipped; roster is missing or inconsistent ({rows.Count} rows).");
                    await tx.CommitAsync();
                    continue;
                }

                var windowScans = scans.Where(x => x.ScannedAtLocal >= scheduled.Window.StartLocal.AddMinutes(-20) &&
                                                   x.ScannedAtLocal <= scheduled.Window.EndLocal.AddHours(1))
                    .ToArray();
                var scanIds = windowScans.Select(x => x.StudentId).Distinct(StringComparer.Ordinal).ToArray();
                var rosterIds = rows.Select(x => x.StudentId).ToHashSet(StringComparer.Ordinal);
                var mapped = scanIds.Count(rosterIds.Contains);
                if (mapped < MinimumMatchedScans || mapped < scanIds.Length * 0.7)
                {
                    Console.WriteLine($"CHAPEL {scheduled.Window.StartLocal:yyyy-MM-dd HH:mm}: skipped; feed coverage is insufficient ({mapped} matched of {scanIds.Length} scan IDs).");
                    await tx.CommitAsync();
                    continue;
                }

                var byStudent = windowScans.GroupBy(x => x.StudentId, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
                if (!apply)
                {
                    var comparison = rows.Select(x => (x.Status, x.Source,
                            Match: ChapelScanMatcher.Match(x.StudentId, scheduled.Window,
                                byStudent.GetValueOrDefault(x.StudentId) ?? []).Outcome))
                        .ToArray();
                    var cbord = comparison.Where(x => x.Source == 1).ToArray();
                    var disagreement = cbord.Count(x =>
                        x.Match == ChapelScanMatchOutcome.Present && x.Status != 1 ||
                        x.Match != ChapelScanMatchOutcome.Present && x.Status == 1);
                    Console.WriteLine($"CHAPEL {scheduled.Window.StartLocal:yyyy-MM-dd HH:mm} comparison: scan present={comparison.Count(x => x.Match == ChapelScanMatchOutcome.Present)}, late={comparison.Count(x => x.Match == ChapelScanMatchOutcome.Late)}, scan-in-only={comparison.Count(x => x.Match == ChapelScanMatchOutcome.ScanInOnly)}, no-pair={comparison.Count(x => x.Match == ChapelScanMatchOutcome.NoQualifyingScan)}, ambiguous={comparison.Count(x => x.Match == ChapelScanMatchOutcome.Ambiguous)}; existing present={rows.Count(x => x.Status == 1)}, absent={rows.Count(x => x.Status == 2)}, excused={rows.Count(x => x.Status == 3)}, pending={rows.Count(x => x.Status == 0)}; CBORD disagreements={disagreement}/{cbord.Length}.");
                }
                var decisions = new List<AttendanceDecision>();
                var ambiguous = 0;
                foreach (var row in rows.Where(x => x.Status == 0 && x.Source is 0 or 3))
                {
                    var match = ChapelScanMatcher.Match(row.StudentId, scheduled.Window,
                        byStudent.GetValueOrDefault(row.StudentId) ?? []);
                    if (match.Outcome == ChapelScanMatchOutcome.Ambiguous)
                    {
                        ambiguous++;
                        continue;
                    }
                    var status = match.Outcome == ChapelScanMatchOutcome.Present ? 1 : 2;
                    var comment = match.Outcome switch
                    {
                        ChapelScanMatchOutcome.Late => "Late",
                        ChapelScanMatchOutcome.ScanInOnly => "Scan In Only",
                        _ => null
                    };
                    decisions.Add(new AttendanceDecision(row.Id, status, comment));
                }

                if (apply && decisions.Count > 0)
                {
                    await using var update = new NpgsqlCommand("""
                        WITH decisions AS (
                          SELECT * FROM unnest(@ids, @statuses, @comments)
                            AS d(id, status, comment)
                        )
                        UPDATE "AppChapelAttendanceRecords" AS attendance
                        SET "Status" = decisions.status,
                            "Comment" = decisions.comment,
                            "Source" = 1,
                            "ExtraProperties" = (attendance."ExtraProperties"::jsonb ||
                              jsonb_build_object('CbordFeedSha256', @sha))::text,
                            "ConcurrencyStamp" = md5(random()::text || clock_timestamp()::text),
                            "LastModificationTime" = (now() AT TIME ZONE 'UTC')
                        FROM decisions
                        WHERE attendance."Id" = decisions.id
                          AND attendance."TenantId" = @tenant
                          AND attendance."Status" = 0 AND attendance."Source" IN (0, 3)
                          AND NOT attendance."IsDeleted"
                        """, db, tx);
                    update.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                        decisions.Select(x => x.Id).ToArray());
                    update.Parameters.AddWithValue("statuses", NpgsqlDbType.Array | NpgsqlDbType.Integer,
                        decisions.Select(x => x.Status).ToArray());
                    update.Parameters.AddWithValue("comments", NpgsqlDbType.Array | NpgsqlDbType.Text,
                        decisions.Select(x => x.Comment).ToArray());
                    update.Parameters.AddWithValue("sha", sha256);
                    update.Parameters.AddWithValue("tenant", tenantId);
                    var changed = await update.ExecuteNonQueryAsync();
                    if (changed != decisions.Count)
                        throw new InvalidOperationException("Chapel attendance changed concurrently; the event was rolled back.");
                }
                await tx.CommitAsync();
                Console.WriteLine($"CHAPEL {scheduled.Window.StartLocal:yyyy-MM-dd HH:mm}: {(apply ? "applied" : "preview")}; present={decisions.Count(x => x.Status == 1)}, absent={decisions.Count(x => x.Status == 2)}, ambiguous={ambiguous}, protected/existing={rows.Count - decisions.Count - ambiguous}. File {sha256[..12]}.");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        if (events.Length == 0)
            Console.WriteLine("CHAPEL: no completed scheduled events in the archived file need processing.");
    }

    private static IReadOnlyList<ScheduledEvent> LoadSchedule()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fall-2026-schedule.tsv");
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2 || lines[0] != "TermCalendarId\tStartLocal\tEndLocal")
            throw new InvalidDataException("The Chapel schedule is missing or malformed.");
        var schedule = new List<ScheduledEvent>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('\t');
            if (cells.Length != 3 || !int.TryParse(cells[0], out var term) || term != 268 ||
                !DateTime.TryParseExact(cells[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start) ||
                !DateTime.TryParseExact(cells[2], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end))
                throw new InvalidDataException("The Chapel schedule contains an invalid event.");
            var window = new ChapelEventWindow(DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
                DateTime.SpecifyKind(end, DateTimeKind.Unspecified));
            window.Validate();
            schedule.Add(new ScheduledEvent(term, window));
        }
        if (schedule.Select(x => x.Window.StartLocal).Distinct().Count() != schedule.Count)
            throw new InvalidDataException("The Chapel schedule contains duplicate events.");
        return schedule;
    }

    private sealed record ScheduledEvent(int TermCalendarId, ChapelEventWindow Window);
    private sealed record AttendanceRow(Guid Id, string StudentId, int Status, int Source);
    private sealed record AttendanceDecision(Guid Id, int Status, string? Comment);
}
