using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CampusFlow.Chapel;
using Npgsql;
using Renci.SshNet;

const string host = "sftp.aws.cbord.com";
const string username = "nelson_1";
const string remoteDirectory = "/nelson.odyssey.cbord.com/Chapel";
const string expectedFileName = "ChapelAttendanceDaily.csv";
const string pinnedEd25519Sha256 = "vUbPhmUOtlZN4EwV/ijjFBqo+SAFShQqQjW/TFpYP+A";
const int maximumFileBytes = 64 * 1024 * 1024;
var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
var nowCentral = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, chicago);
if (args.Length == 2 && args[0] == "--preview-file")
{
    var previewBytes = File.ReadAllBytes(args[1]);
    var (previewHash, previewCount) = ValidateFile(previewBytes, nowCentral.DateTime);
    Console.WriteLine($"PREVIEW ONLY: SHA-256 {previewHash}, {previewCount} scans. No SFTP or database connection was made.");
    return;
}
var force = args.Length == 1 && args[0] == "--manual";
var probe = args.Length == 1 && args[0] == "--probe";
if (args.Length > 0 && !force && !probe)
    throw new ArgumentException("Only --manual, --probe, or --preview-file is supported.");

// Two UTC trigger times cover both sides of the daylight-saving transition. Only
// the trigger that is 1:30 p.m. Chicago time is allowed to fetch a file.
if (!force && !probe && (nowCentral.DayOfWeek is not (DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday) ||
               nowCentral.TimeOfDay < new TimeSpan(13, 30, 0) ||
               nowCentral.TimeOfDay >= new TimeSpan(13, 50, 0)))
{
    Console.WriteLine("Outside the Wednesday–Friday 1:30 p.m. Central import window; no work performed.");
    return;
}

if (!probe && Environment.GetEnvironmentVariable("ChapelFeed__Enabled") != "true")
{
    Console.WriteLine("Chapel SFTP feed is disabled; no connection was made.");
    return;
}

var password = Environment.GetEnvironmentVariable("ChapelSftp__Password");
if (string.IsNullOrWhiteSpace(password) || password == "REPLACE_WITH_CBORD_PASSWORD")
    throw new InvalidOperationException("ChapelSftp__Password has not been configured.");

if (probe)
{
    using var connection = OpenSftp(password);
    var names = connection.ListDirectory(remoteDirectory)
        .Where(file => file.IsRegularFile && file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        .Select(file => file.Name).ToArray();
    Console.WriteLine($"SFTP PROBE: pinned host key accepted; {names.Length} CSV file(s), expected filename present: {names.Contains(expectedFileName, StringComparer.Ordinal)}. No file was downloaded or changed.");
    return;
}

var postgres = Environment.GetEnvironmentVariable("POSTGRESQLCONNSTR_Default") ??
               Environment.GetEnvironmentVariable("ConnectionStrings__Default");
if (string.IsNullOrWhiteSpace(postgres))
    throw new InvalidOperationException("The default PostgreSQL connection is not configured.");
var expectedDatabase = Environment.GetEnvironmentVariable("ChapelFeed__ExpectedDatabase");
var expectedHost = Environment.GetEnvironmentVariable("ChapelFeed__ExpectedHost");
if (string.IsNullOrWhiteSpace(expectedDatabase) || string.IsNullOrWhiteSpace(expectedHost) ||
    !string.Equals(new NpgsqlConnectionStringBuilder(postgres).Host, expectedHost,
        StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The expected PostgreSQL host/database must be configured explicitly.");

byte[] content;
using (var sftp = OpenSftp(password))
{
    var csvFiles = sftp.ListDirectory(remoteDirectory)
        .Where(file => file.IsRegularFile && file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (csvFiles.Length != 1 || csvFiles[0].Name != expectedFileName)
        throw new InvalidDataException("The CBORD directory must contain exactly the expected ChapelAttendanceDaily.csv file. Nothing was downloaded or removed.");
    if (csvFiles[0].Length is <= 0 or > maximumFileBytes)
        throw new InvalidDataException("The CBORD file is empty or exceeds the configured size limit.");

    using var remote = sftp.OpenRead(csvFiles[0].FullName);
    using var local = new MemoryStream((int)csvFiles[0].Length);
    var buffer = new byte[64 * 1024];
    int read;
    while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
    {
        if (local.Length + read > maximumFileBytes)
            throw new InvalidDataException("The CBORD file grew beyond the configured size limit during download.");
        local.Write(buffer, 0, read);
    }
    content = local.ToArray();
    sftp.Disconnect();
}

var (sha256, scanCount) = ValidateFile(content, nowCentral.DateTime);

await using var db = new NpgsqlConnection(postgres);
await db.OpenAsync();
await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    await using (var target = new NpgsqlCommand("SELECT current_database()", db, transaction))
    {
        var actual = (string?)await target.ExecuteScalarAsync();
        if (actual != expectedDatabase)
            throw new InvalidOperationException("The destination database is not the configured Chapel feed database.");
    }
    Guid tenantId;
    await using (var tenant = new NpgsqlCommand("SELECT \"Id\" FROM \"AbpTenants\" WHERE \"NormalizedName\" = 'NELSON' AND NOT \"IsDeleted\"", db, transaction))
    {
        tenantId = await tenant.ExecuteScalarAsync() is Guid id ? id :
            throw new InvalidOperationException("The Nelson tenant is not present in the destination database.");
    }
    await using (var migration = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261028034957_AddChapelFeedFiles'", db, transaction))
    {
        if (Convert.ToInt32(await migration.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The Chapel feed schema has not been migrated.");
    }
    await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(20261028, 13030)", db, transaction))
        await guard.ExecuteNonQueryAsync();

    await using var insert = new NpgsqlCommand("""
        INSERT INTO "AppChapelFeedFiles"
          ("Id", "TenantId", "FileName", "Sha256", "Content", "ScanCount",
           "ReceivedAtUtc", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
        VALUES
          (@id, @tenant, @name, @sha, @content, @count,
           @received, '{}', @stamp, @created, false)
        ON CONFLICT ("TenantId", "Sha256") DO NOTHING
        """, db, transaction);
    insert.Parameters.AddWithValue("id", Guid.NewGuid());
    insert.Parameters.AddWithValue("tenant", tenantId);
    insert.Parameters.AddWithValue("name", expectedFileName);
    insert.Parameters.AddWithValue("sha", sha256);
    insert.Parameters.AddWithValue("content", content);
    insert.Parameters.AddWithValue("count", scanCount);
    insert.Parameters.AddWithValue("received", DateTime.UtcNow);
    insert.Parameters.AddWithValue("created", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified));
    insert.Parameters.AddWithValue("stamp", Guid.NewGuid().ToString("N"));
    var inserted = await insert.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    Console.WriteLine(inserted == 1
        ? $"ARCHIVED CBORD file: SHA-256 {sha256}, {scanCount} scans. Remote file was retained. Attendance was not changed."
        : $"ALREADY ARCHIVED: SHA-256 {sha256}. Remote file was retained. Attendance was not changed.");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

static (string Sha256, int ScanCount) ValidateFile(byte[] content, DateTime nowCentral)
{
    if (content.Length is 0 or > maximumFileBytes)
        throw new InvalidDataException("The CBORD file is empty or exceeds the configured size limit.");
    var count = 0;
    using var csv = new StreamReader(new MemoryStream(content), new UTF8Encoding(false, true),
        detectEncodingFromByteOrderMarks: true);
    foreach (var scan in ChapelScanCsvReader.ReadRows(csv))
    {
        if (scan.ScannedAtLocal > nowCentral.AddDays(1) ||
            scan.ScannedAtLocal < nowCentral.AddDays(-180))
            throw new InvalidDataException($"CBORD scan row {scan.RowNumber} is outside the allowed date range.");
        count++;
    }
    if (count == 0) throw new InvalidDataException("The CBORD file contains no scans.");
    return (Convert.ToHexString(SHA256.HashData(content)), count);
}

static SftpClient OpenSftp(string password)
{
    var client = new SftpClient(host, username, password);
    client.HostKeyReceived += (_, key) =>
        key.CanTrust = key.HostKeyName == "ssh-ed25519" &&
                       key.FingerPrintSHA256 == pinnedEd25519Sha256;
    try
    {
        client.Connect();
        return client;
    }
    catch
    {
        client.Dispose();
        throw;
    }
}
