using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using CampusFlow.BillApprovals;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

// Preview by default. Source rows and credentials are never written to the console.
try
{
    string? Option(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    var source = Option("--source") ?? throw new ArgumentException("--source is required.");
    var tenantId = Guid.Parse(Option("--tenant") ?? throw new ArgumentException("--tenant is required."));
    if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.");
    var apply = args.Contains("--apply");
    var sourceBytes = File.ReadAllBytes(source);
    var hash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
    using var input = new StringReader(Encoding.UTF8.GetString(sourceBytes));
    var rows = LegacyBillApprovalFile.Read(input);
    if (apply && (Option("--expected-sha256") != hash || Option("--expected-rows") != rows.Count.ToString(CultureInfo.InvariantCulture)))
        throw new InvalidOperationException("Apply requires the reviewed source hash and row count.");

    IConfiguration config;
    if (Option("--configuration") is { } file)
        config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(file)).Build();
    else if (Option("--azure-app") is { } webapp && Option("--resource-group") is { } group)
    {
        var values = new Dictionary<string, string?>();
        foreach (var item in (await Azure("webapp", "config", "appsettings", "list", "--name", webapp, "--resource-group", group, "-o", "json")).EnumerateArray())
            values[item.GetProperty("name").GetString()!.Replace("__", ":")] = item.GetProperty("value").GetString();
        foreach (var item in (await Azure("webapp", "config", "connection-string", "list", "--name", webapp, "--resource-group", group, "-o", "json")).EnumerateArray())
            values["ConnectionStrings:" + item.GetProperty("name").GetString()] = item.GetProperty("value").GetString();
        config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    else throw new ArgumentException("Provide --configuration or --azure-app and --resource-group.");

    var students = new Dictionary<int, string>();
    var terms = new Dictionary<int, (string Code, string Name)>();
    await using (var replica = new SqlConnection(config.GetConnectionString("ThesisElementsReadOnly")))
    {
        await replica.OpenAsync();
        await using (var command = new SqlCommand("SELECT s.StudentUID,s.StudentID FROM dbo.CAMS_Student_View s JOIN OPENJSON(@ids) WITH (Id int '$') i ON s.StudentUID=i.Id", replica) { CommandTimeout = 120 })
        {
            command.Parameters.Add("@ids", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows.Select(x => x.StudentUid).Distinct());
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) students.Add(reader.GetInt32(0), Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!);
        }
        await using (var command = new SqlCommand("SELECT t.TermCalendarID,t.Term,t.TextTerm FROM dbo.TermCalendar t JOIN OPENJSON(@ids) WITH (Id int '$') i ON t.TermCalendarID=i.Id", replica) { CommandTimeout = 120 })
        {
            command.Parameters.Add("@ids", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows.Select(x => x.TermCalendarId).Distinct());
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) terms.Add(reader.GetInt32(0), (Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!, Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture)!));
        }
    }
    if (rows.Any(r => !students.ContainsKey(r.StudentUid) || string.IsNullOrWhiteSpace(students[r.StudentUid]) || !terms.ContainsKey(r.TermCalendarId)))
        throw new InvalidOperationException("Unmatched or ambiguous student/term mappings must be resolved before import.");
    var entities = rows.Select(r => new LegacyBillApproval(Guid.NewGuid(), tenantId,
        r.StudentUid.ToString(CultureInfo.InvariantCulture), students[r.StudentUid], r.TermCalendarId.ToString(CultureInfo.InvariantCulture),
        terms[r.TermCalendarId].Code, terms[r.TermCalendarId].Name, r.Approved, r.LoginTime, r.AcceptTime, hash, r.RowNumber)).ToArray();

    await using var connection = new NpgsqlConnection(config.GetConnectionString("Default"));
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    async Task<object?> Scalar(string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        return await command.ExecuteScalarAsync();
    }
    if (Convert.ToInt64(await Scalar("SELECT count(*) FROM \"AbpTenants\" WHERE \"Id\"=@tenant AND \"IsDeleted\"=false")) != 1)
        throw new InvalidOperationException("Target tenant does not exist.");
    var keys = entities.Select(e => (e.ExternalStudentId, e.ExternalTermId)).ToHashSet();
    var overlaps = 0;
    await using (var command = new NpgsqlCommand("SELECT \"ExternalStudentId\",\"ExternalTermId\" FROM \"AppBillApprovals\" WHERE \"TenantId\"=@tenant AND \"IsDeleted\"=false", connection, transaction))
    {
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (keys.Contains((reader.GetString(0), reader.GetString(1)))) overlaps++;
    }
    var exists = (bool)(await Scalar("SELECT to_regclass('public.\"AppLegacyBillApprovals\"') IS NOT NULL"))!;
    var existing = new Dictionary<(string, string), string>();
    string Signature(string studentId, string termCode, string termName, bool approved, DateTime? login, DateTime? accept, bool deleted) =>
        JsonSerializer.Serialize(new { studentId, termCode, termName, approved, login, accept, deleted });
    if (exists)
    {
        // Serialize concurrent imports; the table's unique key is an additional safeguard.
        if (apply) await Scalar("SELECT pg_advisory_xact_lock(hashtext('CampusFlow.LegacyBillApprovalImport'),hashtext(@tenant::text))");
        await using var command = new NpgsqlCommand("SELECT \"ExternalStudentId\",\"ExternalTermId\",\"StudentId\",\"TermCode\",\"TermName\",\"SourceApproved\",\"LoginTime\",\"AcceptTime\",\"IsDeleted\" FROM \"AppLegacyBillApprovals\" WHERE \"TenantId\"=@tenant", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) existing.Add((reader.GetString(0), reader.GetString(1)), Signature(reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6), reader.IsDBNull(7) ? null : reader.GetDateTime(7), reader.GetBoolean(8)));
    }
    var additions = entities.Where(e => !existing.ContainsKey((e.ExternalStudentId, e.ExternalTermId))).ToArray();
    var conflicts = entities.Count(e => existing.TryGetValue((e.ExternalStudentId, e.ExternalTermId), out var value) && value != Signature(e.StudentId, e.TermCode, e.TermName, e.SourceApproved, e.LoginTime, e.AcceptTime, false));
    Console.WriteLine(JsonSerializer.Serialize(new { mode = apply ? "apply" : "preview", sourceSha256 = hash, rows = rows.Count,
        approved = rows.Count(r => r.Approved), unapproved = rows.Count(r => !r.Approved), missingApprovedDate = rows.Count(r => r.Approved && r.AcceptTime == null),
        unspecifiedTerm = rows.Count(r => r.TermCalendarId == 0), students = students.Count, terms = terms.Count,
        nativeOverlap = overlaps, schemaReady = exists, wouldInsert = additions.Length, unchanged = rows.Count - additions.Length - conflicts, conflicts }));
    if (!apply) return;
    if (!exists || conflicts > 0) throw new InvalidOperationException("Schema or import conflicts must be resolved. No changes made.");
    if (additions.Length > 0)
    {
        await using (var command = new NpgsqlCommand("CREATE TEMP TABLE legacy_bill_stage (LIKE \"AppLegacyBillApprovals\" INCLUDING DEFAULTS) ON COMMIT DROP", connection, transaction))
            await command.ExecuteNonQueryAsync();
        const string columns = "\"Id\",\"TenantId\",\"ExternalStudentId\",\"StudentId\",\"ExternalTermId\",\"TermCode\",\"TermName\",\"SourceApproved\",\"LoginTime\",\"AcceptTime\",\"SourceSha256\",\"SourceRowNumber\",\"ExtraProperties\",\"ConcurrencyStamp\",\"CreationTime\",\"IsDeleted\"";
        await using (var copy = await connection.BeginBinaryImportAsync("COPY legacy_bill_stage (" + columns + ") FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var e in additions)
            {
                await copy.StartRowAsync();
                await copy.WriteAsync(e.Id, NpgsqlDbType.Uuid); await copy.WriteAsync(tenantId, NpgsqlDbType.Uuid);
                foreach (var value in new[] { e.ExternalStudentId, e.StudentId, e.ExternalTermId, e.TermCode, e.TermName }) await copy.WriteAsync(value, NpgsqlDbType.Varchar);
                await copy.WriteAsync(e.SourceApproved, NpgsqlDbType.Boolean);
                if (e.LoginTime.HasValue) await copy.WriteAsync(e.LoginTime.Value, NpgsqlDbType.Timestamp); else await copy.WriteNullAsync();
                if (e.AcceptTime.HasValue) await copy.WriteAsync(e.AcceptTime.Value, NpgsqlDbType.Timestamp); else await copy.WriteNullAsync();
                await copy.WriteAsync(hash, NpgsqlDbType.Varchar); await copy.WriteAsync(e.SourceRowNumber, NpgsqlDbType.Integer);
                await copy.WriteAsync("{}", NpgsqlDbType.Text); await copy.WriteAsync(e.ConcurrencyStamp, NpgsqlDbType.Varchar);
                await copy.WriteAsync(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), NpgsqlDbType.Timestamp); await copy.WriteAsync(false, NpgsqlDbType.Boolean);
            }
            await copy.CompleteAsync();
        }
        await using var insert = new NpgsqlCommand("INSERT INTO \"AppLegacyBillApprovals\" (" + columns + ") SELECT " + columns + " FROM legacy_bill_stage", connection, transaction) { CommandTimeout = 120 };
        var inserted = await insert.ExecuteNonQueryAsync();
        if (inserted != additions.Length) throw new InvalidOperationException("Import count mismatch; transaction rolled back.");
    }
    await transaction.CommitAsync();
    Console.WriteLine("Import committed. Existing CampusFlow approvals and external systems were not modified.");
}
catch (Exception exception)
{
    Console.Error.WriteLine("Legacy import stopped: " + exception.GetType().Name + ". Sensitive error details suppressed; no partial transaction committed.");
    Environment.ExitCode = 1;
}

static async Task<JsonElement> Azure(params string[] arguments)
{
    var info = new ProcessStartInfo("az") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); await error;
    if (process.ExitCode != 0) throw new InvalidOperationException("Azure configuration lookup failed.");
    using var document = JsonDocument.Parse(await output); return document.RootElement.Clone();
}
