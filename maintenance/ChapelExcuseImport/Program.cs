using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

var options = ParseOptions(args);
var input = Required(options, "input");
var term = int.Parse(Required(options, "term"), CultureInfo.InvariantCulture);
if (term <= 0) throw new ArgumentException("Term must be positive.");
var lines = File.ReadAllLines(input);
if (lines.Length < 2 || lines[0].TrimEnd('\r') !=
    "SourceTable\tStudentUID\tTermCalendarID\tStartDate\tEndDate\tExcuseType\tReason\tInsertTime")
    throw new InvalidDataException("Unexpected Chapel excuse export header.");
var rows = new List<LegacyRequest>();
var keys = new HashSet<string>(StringComparer.Ordinal);
foreach (var (line, index) in lines.Skip(1).Select((line, index) => (line.TrimEnd('\r'), index + 2)))
{
    var fields = line.Split('\t');
    if (fields.Length != 8 || fields[0] != "CAMs_Enterprise.dbo.SAGU_ChapelExcuseRequests")
        throw new InvalidDataException($"Unexpected Chapel excuse export row {index}.");
    var uid = int.Parse(fields[1], CultureInfo.InvariantCulture);
    var rowTerm = int.Parse(fields[2], CultureInfo.InvariantCulture);
    if (uid <= 0 || rowTerm != term) throw new InvalidDataException($"Wrong student or term at row {index}.");
    var start = DateOnly.FromDateTime(DateTime.Parse(fields[3], CultureInfo.InvariantCulture));
    var end = DateOnly.FromDateTime(DateTime.Parse(fields[4], CultureInfo.InvariantCulture));
    if (end < start)
        throw new InvalidDataException($"Unexpected date span at row {index}.");
    if (fields[5] is not ("Work" or "MedicalIssue" or "Other") || fields[6].Length > 2000)
        throw new InvalidDataException($"Invalid category or reason at row {index}.");
    var submittedLocal = DateTime.SpecifyKind(DateTime.Parse(fields[7], CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)));
    if (!keys.Add(key)) throw new InvalidDataException($"Duplicate source row {index}.");
    rows.Add(new LegacyRequest(uid, start, end, fields[5], fields[6], submittedLocal, key));
}
var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
Console.WriteLine($"Preview: term {term}, {rows.Count} requests for {rows.Select(x => x.StudentUid).Distinct().Count()} students; {rows.Count(x => x.End.DayNumber - x.Start.DayNumber > 2)} exceed three days; {rows.Count(x => string.IsNullOrWhiteSpace(x.Reason))} have no reason; SHA-256 {checksum}.");
if (!options.ContainsKey("execute"))
{
    Console.WriteLine("PREVIEW ONLY: no database connection or write.");
    return;
}
if (options["execute"] != "true" ||
    !string.Equals(Required(options, "sha256"), checksum, StringComparison.OrdinalIgnoreCase) ||
    int.Parse(Required(options, "count"), CultureInfo.InvariantCulture) != rows.Count ||
    Required(options, "confirm") != $"IMPORT CHAPEL EXCUSES {term}")
    throw new InvalidOperationException("Execution requires the exact preview checksum, count, and confirmation phrase.");
var connectionString = Environment.GetEnvironmentVariable("CAMPUSFLOW_CHAPEL_POSTGRES");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set CAMPUSFLOW_CHAPEL_POSTGRES securely; do not pass it as an argument.");
var expectedHost = Required(options, "host");
var expectedDatabase = Required(options, "database");
var expectedTenant = Required(options, "tenant");
if (!string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Host, expectedHost, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Target PostgreSQL host does not match --host.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    if (!string.Equals((string?)await Scalar("SELECT current_database()"), expectedDatabase, StringComparison.Ordinal))
        throw new InvalidOperationException("Target PostgreSQL database does not match --database.");
    await using var tenantCommand = new NpgsqlCommand("SELECT \"Id\" FROM \"AbpTenants\" WHERE \"NormalizedName\" = upper(@name) AND NOT \"IsDeleted\"", connection, transaction);
    tenantCommand.Parameters.AddWithValue("name", expectedTenant);
    var tenantId = (Guid?)await tenantCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Tenant not found.");
    if (Convert.ToInt64(await Scalar("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261028021912_AddChapelExcuseRequests'"), CultureInfo.InvariantCulture) != 1)
        throw new InvalidOperationException("The Chapel excuse schema migration is not applied.");
    await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(20261028, @term)", connection, transaction))
    {
        guard.Parameters.AddWithValue("term", term);
        await guard.ExecuteNonQueryAsync();
    }
    var students = new Dictionary<int, (string Id, string Name)>();
    await using (var lookup = new NpgsqlCommand("SELECT DISTINCT \"StudentUid\", \"StudentId\", \"StudentName\" FROM \"AppChapelAttendanceRecords\" WHERE \"TenantId\" = @tenant AND \"TermCalendarId\" = @term", connection, transaction))
    {
        lookup.Parameters.AddWithValue("tenant", tenantId);
        lookup.Parameters.AddWithValue("term", term);
        await using var reader = await lookup.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var uid = reader.GetInt32(0);
            if (!students.TryAdd(uid, (reader.GetString(1), reader.GetString(2))))
                throw new InvalidOperationException("Conflicting student identity in imported Chapel attendance.");
        }
    }
    if (rows.Any(x => !students.ContainsKey(x.StudentUid)))
        throw new InvalidOperationException("A request student is missing from validated Chapel attendance history.");
    var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    var inserted = 0;
    foreach (var row in rows)
    {
        await using var exists = new NpgsqlCommand("SELECT count(*) FROM \"AppChapelExcuseRequests\" WHERE \"TenantId\" = @tenant AND \"ExtraProperties\"::jsonb ->> 'LegacySourceKey' = @key", connection, transaction);
        exists.Parameters.AddWithValue("tenant", tenantId);
        exists.Parameters.AddWithValue("key", row.Key);
        var prior = Convert.ToInt64(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (prior > 1) throw new InvalidOperationException("Duplicate legacy request key in destination.");
        if (prior == 1) continue;
        var student = students[row.StudentUid];
        var submittedUtc = TimeZoneInfo.ConvertTimeToUtc(row.SubmittedLocal, zone);
        var extra = JsonSerializer.Serialize(new { LegacyImport = true, LegacySourceKey = row.Key, LegacyInsertTime = row.SubmittedLocal.ToString("O", CultureInfo.InvariantCulture) });
        await using var insert = new NpgsqlCommand("""
            INSERT INTO "AppChapelExcuseRequests"
              ("Id", "TenantId", "TermCalendarId", "StudentUid", "StudentId", "StudentName",
               "StartDate", "EndDate", "ExcuseType", "Reason", "ExtraProperties",
               "ConcurrencyStamp", "CreationTime", "IsDeleted")
            VALUES (@id, @tenant, @term, @uid, @studentId, @studentName,
                    @start, @end, @category, @reason, @extra, @stamp, @created, false)
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("tenant", tenantId);
        insert.Parameters.AddWithValue("term", term);
        insert.Parameters.AddWithValue("uid", row.StudentUid);
        insert.Parameters.AddWithValue("studentId", student.Id);
        insert.Parameters.AddWithValue("studentName", student.Name);
        insert.Parameters.AddWithValue("start", row.Start);
        insert.Parameters.AddWithValue("end", row.End);
        insert.Parameters.AddWithValue("category", row.Category);
        insert.Parameters.AddWithValue("reason", row.Reason);
        insert.Parameters.AddWithValue("extra", extra);
        insert.Parameters.AddWithValue("stamp", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("created", DateTime.SpecifyKind(submittedUtc, DateTimeKind.Unspecified));
        inserted += await insert.ExecuteNonQueryAsync();
    }
    if (inserted == 0)
    {
        Console.WriteLine("ALREADY IMPORTED: all source rows present; no application data changed.");
        await transaction.RollbackAsync();
        return;
    }
    await transaction.CommitAsync();
    Console.WriteLine($"COMMITTED: {inserted} historical Chapel excuse requests to {expectedDatabase}, tenant {expectedTenant}, term {term}.");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

async Task<object?> Scalar(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return await command.ExecuteScalarAsync();
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length; i++)
    {
        if (!arguments[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unexpected argument.");
        var key = arguments[i][2..];
        var value = key == "execute" ? "true" : i + 1 < arguments.Length ? arguments[++i] : throw new ArgumentException($"Missing value for {key}.");
        if (!result.TryAdd(key, value)) throw new ArgumentException($"Duplicate option {key}.");
    }
    return result;
}
static string Required(Dictionary<string, string> values, string key) =>
    values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing --{key}.");

sealed record LegacyRequest(int StudentUid, DateOnly Start, DateOnly End,
    string Category, string Reason, DateTime SubmittedLocal, string Key);
