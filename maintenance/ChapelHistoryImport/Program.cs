using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CampusFlow.Chapel;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

// Deliberately separate from EF migrations: this imports a point-in-time, protected
// data export only after the chapel schema migration has been applied.
var options = ParseOptions(args);
var historyPath = Required(options, "history");
var schedulePath = Required(options, "schedule");
var term = int.Parse(Required(options, "term"), CultureInfo.InvariantCulture);
if (term <= 0) throw new ArgumentException("Term must be positive.");

var schedule = ReadSchedule(schedulePath, term);
var rows = new List<ChapelLegacyAttendanceRow>();
var ids = new HashSet<int>();
var keys = new HashSet<(int StudentUid, DateTime ChapelAtLocal)>();
var students = new Dictionary<string, int>(StringComparer.Ordinal);
var uids = new Dictionary<int, string>();
var eventCounts = new Dictionary<DateTime, int>();
var statusCounts = new int[4];
var conflicts = 0;
using (var reader = File.OpenText(historyPath))
{
    foreach (var row in ChapelLegacyAttendanceTsvReader.ReadRows(reader))
    {
        if (row.TermCalendarId != term || !schedule.Contains(row.ChapelAtLocal))
            throw new InvalidDataException($"History row {row.RowNumber} is outside the requested term or schedule.");
        if (!ids.Add(row.LegacyAttendanceId) || !keys.Add((row.LegacyStudentUid, row.ChapelAtLocal)))
            throw new InvalidDataException($"History row {row.RowNumber} duplicates an attendance identity.");
        if (students.TryGetValue(row.StudentId, out var oldUid) && oldUid != row.LegacyStudentUid ||
            uids.TryGetValue(row.LegacyStudentUid, out var oldId) && oldId != row.StudentId)
            throw new InvalidDataException($"History row {row.RowNumber} has a conflicting student identity.");
        if (StudentName(row).Length > 256)
            throw new InvalidDataException($"History row {row.RowNumber} has a name over 256 characters.");
        students[row.StudentId] = row.LegacyStudentUid;
        uids[row.LegacyStudentUid] = row.StudentId;
        rows.Add(row);
        eventCounts[row.ChapelAtLocal] = eventCounts.GetValueOrDefault(row.ChapelAtLocal) + 1;
        statusCounts[EffectiveStatus(row)]++;
        if (row.HasConflictingFlags) conflicts++;
    }
}
if (rows.Count == 0 || eventCounts.Count != schedule.Count)
    throw new InvalidDataException("The history export does not cover every scheduled event.");
using var historyStream = File.OpenRead(historyPath);
var hash = Convert.ToHexString(SHA256.HashData(historyStream));
Console.WriteLine($"History preview: term {term}, {rows.Count} rows, {eventCounts.Count} events, {students.Count} students.");
Console.WriteLine($"Rows per event: {eventCounts.Values.Min()}–{eventCounts.Values.Max()}; Present {statusCounts[1]}, Absent {statusCounts[2]}, Excused {statusCounts[3]}, Pending {statusCounts[0]}.");
Console.WriteLine($"Conflicting legacy flags: {conflicts}; SHA-256: {hash}");

if (!options.ContainsKey("execute"))
{
    Console.WriteLine("PREVIEW ONLY: no database connections or writes were made.");
    return;
}

if (options.Count != 10 || options["execute"] != "true" ||
    !string.Equals(Required(options, "sha256"), hash, StringComparison.OrdinalIgnoreCase) ||
    int.Parse(Required(options, "count"), CultureInfo.InvariantCulture) != rows.Count ||
    Required(options, "confirm") != $"IMPORT CHAPEL HISTORY {term}")
    throw new InvalidOperationException("Execution requires the exact preview hash, row count, and confirmation phrase.");

var expectedDatabase = Required(options, "database");
var expectedHost = Required(options, "host");
var expectedTenant = Required(options, "tenant");
var elementsConnection = Environment.GetEnvironmentVariable("CAMPUSFLOW_CHAPEL_ELEMENTS_READONLY");
var postgresConnection = Environment.GetEnvironmentVariable("CAMPUSFLOW_CHAPEL_POSTGRES");
if (string.IsNullOrWhiteSpace(elementsConnection) || string.IsNullOrWhiteSpace(postgresConnection))
    throw new InvalidOperationException("Set both chapel import connection environment variables. Do not pass credentials on the command line.");
if (!string.Equals(new NpgsqlConnectionStringBuilder(postgresConnection).Host, expectedHost,
        StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Target PostgreSQL host does not match --host.");

await VerifyElementsStudents(students, elementsConnection);
Console.WriteLine("Elements identity check: every StudentID has exactly one matching StudentUID.");

await using var connection = new NpgsqlConnection(postgresConnection);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    var actualDatabase = (string)(await Scalar(connection, transaction, "SELECT current_database()") ?? "");
    if (!string.Equals(actualDatabase, expectedDatabase, StringComparison.Ordinal))
        throw new InvalidOperationException($"Target database mismatch: connected to {actualDatabase}.");
    var tenantId = await TenantId(connection, transaction, expectedTenant);
    var migration = await Scalar(connection, transaction,
        "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261027233041_AddChapelAttendanceFoundation'");
    if (Convert.ToInt64(migration, CultureInfo.InvariantCulture) != 1)
        throw new InvalidOperationException("The chapel schema migration has not been applied to this database.");

    await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(20261027, @term)", connection, transaction))
    {
        guard.Parameters.AddWithValue("term", term);
        await guard.ExecuteNonQueryAsync();
    }
    await using (var create = new NpgsqlCommand("""
        CREATE TEMP TABLE chapel_import_stage (
            id uuid NOT NULL,
            legacy_id integer NOT NULL PRIMARY KEY,
            student_uid integer NOT NULL,
            student_id varchar(64) NOT NULL,
            student_name varchar(256) NOT NULL,
            chapel_at timestamp without time zone NOT NULL,
            status integer NOT NULL,
            comment varchar(4000),
            extra_properties jsonb NOT NULL,
            UNIQUE(student_uid, chapel_at)
        ) ON COMMIT DROP
        """, connection, transaction))
        await create.ExecuteNonQueryAsync();

    await using (var writer = await connection.BeginBinaryImportAsync("""
        COPY chapel_import_stage (id, legacy_id, student_uid, student_id, student_name, chapel_at, status, comment, extra_properties)
        FROM STDIN (FORMAT BINARY)
        """))
    {
        foreach (var row in rows)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
            await writer.WriteAsync(row.LegacyAttendanceId, NpgsqlDbType.Integer);
            await writer.WriteAsync(row.LegacyStudentUid, NpgsqlDbType.Integer);
            await writer.WriteAsync(row.StudentId, NpgsqlDbType.Varchar);
            await writer.WriteAsync(StudentName(row), NpgsqlDbType.Varchar);
            await writer.WriteAsync(row.ChapelAtLocal, NpgsqlDbType.Timestamp);
            await writer.WriteAsync(EffectiveStatus(row), NpgsqlDbType.Integer);
            if (row.Comment is null) await writer.WriteNullAsync();
            else await writer.WriteAsync(row.Comment, NpgsqlDbType.Varchar);
            var extra = JsonSerializer.Serialize(new
            {
                LegacyPresent = row.Present,
                LegacyAbsent = row.Absent,
                LegacyExcused = row.Excused,
                LegacyFlagConflict = row.HasConflictingFlags
            });
            await writer.WriteAsync(extra, NpgsqlDbType.Jsonb);
        }
        await writer.CompleteAsync();
    }

    var existing = await CountForTerm(connection, transaction, tenantId, term);
    if (existing != 0)
    {
        if (existing != rows.Count || await CountMismatches(connection, transaction, tenantId, term) != 0)
            throw new InvalidOperationException($"Found {existing} existing chapel rows for this tenant/term that do not exactly match the import; no changes made.");
        Console.WriteLine("ALREADY IMPORTED: all rows match the source; no application data was changed.");
        await transaction.RollbackAsync();
        return;
    }

    await using (var insert = new NpgsqlCommand("""
        INSERT INTO "AppChapelAttendanceRecords"
          ("Id", "TenantId", "TermCalendarId", "StudentUid", "StudentId", "StudentName",
           "ChapelAtLocal", "Status", "Comment", "Source", "LegacyAttendanceId",
           "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        SELECT id, @tenant, @term, student_uid, student_id, student_name,
               chapel_at, status, comment, 0, legacy_id, extra_properties::text,
               md5(random()::text || clock_timestamp()::text),
               CURRENT_TIMESTAMP AT TIME ZONE 'UTC', false
        FROM chapel_import_stage
        """, connection, transaction))
    {
        insert.Parameters.AddWithValue("tenant", tenantId);
        insert.Parameters.AddWithValue("term", term);
        var inserted = await insert.ExecuteNonQueryAsync();
        if (inserted != rows.Count) throw new InvalidOperationException($"Expected {rows.Count} inserted rows, got {inserted}.");
    }
    if (await CountForTerm(connection, transaction, tenantId, term) != rows.Count ||
        await CountMismatches(connection, transaction, tenantId, term) != 0)
        throw new InvalidOperationException("Post-import reconciliation failed; transaction will be rolled back.");
    await transaction.CommitAsync();
    Console.WriteLine($"COMMITTED: {rows.Count} chapel history rows in database {actualDatabase}, tenant {expectedTenant}, term {term}.");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Use named --options only.");
        var name = args[i][2..];
        if (!result.TryAdd(name, name == "execute" ? "true" : i + 1 < args.Length ? args[++i] :
                throw new ArgumentException($"Missing value for --{name}.")))
            throw new ArgumentException($"Duplicate option --{name}.");
    }
    var allowed = new HashSet<string>(StringComparer.Ordinal)
        { "history", "schedule", "term", "execute", "sha256", "count", "host", "database", "tenant", "confirm" };
    if (result.Keys.Except(allowed).Any()) throw new ArgumentException("Unknown option.");
    return result;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value :
        throw new ArgumentException($"Missing --{name}.");

static HashSet<DateTime> ReadSchedule(string path, int term)
{
    var result = new HashSet<DateTime>();
    using var reader = File.OpenText(path);
    if (reader.ReadLine()?.TrimStart('\uFEFF').StartsWith("TextTerm\tTermCalendarID\tChapelScheduleID\t", StringComparison.Ordinal) != true)
        throw new InvalidDataException("Unexpected schedule export header.");
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        var columns = line.Split('\t');
        if (columns.Length != 8 || !int.TryParse(columns[1], out var rowTerm) || rowTerm != term ||
            !DateTime.TryParseExact(columns[3], "yyyy-MM-dd HH:mm:ss.FFF", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            !DateTime.TryParseExact(columns[4], "yyyy-MM-dd HH:mm:ss.FFF", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            throw new InvalidDataException("Malformed schedule export row or wrong term.");
        if (!result.Add(date.Date.Add(time.TimeOfDay)))
            throw new InvalidDataException("Duplicate scheduled chapel start.");
    }
    if (result.Count == 0) throw new InvalidDataException("Empty chapel schedule export.");
    return result;
}

static int EffectiveStatus(ChapelLegacyAttendanceRow row) =>
    row.Present ? 1 : row.Absent ? 2 : row.Excused ? 3 : 0;

static string StudentName(ChapelLegacyAttendanceRow row) =>
    string.Join(' ', new[] { row.FirstName, row.MiddleName, row.LastName }
        .Where(x => !string.IsNullOrWhiteSpace(x)));

static async Task VerifyElementsStudents(Dictionary<string, int> expected, string connectionString)
{
    var builder = new SqlConnectionStringBuilder(connectionString)
    {
        ApplicationIntent = ApplicationIntent.ReadOnly,
        ConnectTimeout = 15
    };
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandTimeout = 90;
    var index = 0;
    var parameters = new List<string>();
    foreach (var studentId in expected.Keys)
    {
        var name = "@p" + index++;
        command.Parameters.Add(name, SqlDbType.VarChar, 64).Value = studentId;
        parameters.Add(name);
    }
    if (index > 2000) throw new InvalidOperationException("Student set exceeds SQL Server parameter limit.");
    command.CommandText = "SELECT LTRIM(RTRIM(StudentID)), StudentUID FROM dbo.Student WHERE StudentID IN (" +
        string.Join(',', parameters) + ")";
    var seen = new HashSet<string>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var studentId = reader.GetString(0);
        var uid = reader.GetInt32(1);
        if (!seen.Add(studentId) || !expected.TryGetValue(studentId, out var expectedUid) || uid != expectedUid)
            throw new InvalidOperationException($"Elements identity mismatch for StudentID {studentId}.");
    }
    if (seen.Count != expected.Count)
        throw new InvalidOperationException($"Only {seen.Count} of {expected.Count} student identities matched Elements.");
}

static async Task<object?> Scalar(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return await command.ExecuteScalarAsync();
}

static async Task<Guid> TenantId(NpgsqlConnection connection, NpgsqlTransaction transaction, string expectedName)
{
    await using var command = new NpgsqlCommand("SELECT \"Id\" FROM \"AbpTenants\" WHERE \"NormalizedName\" = upper(@name) AND NOT \"IsDeleted\"", connection, transaction);
    command.Parameters.AddWithValue("name", expectedName);
    var value = await command.ExecuteScalarAsync();
    return value is Guid id ? id : throw new InvalidOperationException($"Tenant {expectedName} was not found in the target database.");
}

static async Task<long> CountForTerm(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, int term)
{
    await using var command = new NpgsqlCommand("SELECT count(*) FROM \"AppChapelAttendanceRecords\" WHERE \"TenantId\" = @tenant AND \"TermCalendarId\" = @term", connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    command.Parameters.AddWithValue("term", term);
    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}

static async Task<long> CountMismatches(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, int term)
{
    await using var command = new NpgsqlCommand("""
        SELECT count(*) FROM chapel_import_stage s
        LEFT JOIN "AppChapelAttendanceRecords" a
          ON a."TenantId" = @tenant AND a."TermCalendarId" = @term AND a."LegacyAttendanceId" = s.legacy_id
        WHERE a."Id" IS NULL OR a."StudentUid" <> s.student_uid OR a."StudentId" <> s.student_id
           OR a."StudentName" <> s.student_name OR a."ChapelAtLocal" <> s.chapel_at
           OR a."Status" <> s.status OR a."Comment" IS DISTINCT FROM s.comment
           OR a."Source" <> 0 OR a."ExtraProperties"::jsonb <> s.extra_properties
        """, connection, transaction);
    command.Parameters.AddWithValue("tenant", tenantId);
    command.Parameters.AddWithValue("term", term);
    return (long)(await command.ExecuteScalarAsync() ?? 0L);
}
