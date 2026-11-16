using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Npgsql;

if (args.Length is < 3 or > 4 || args[0] is not ("nonprod" or "production") || args[1] is not ("preview" or "apply"))
    throw new ArgumentException("Usage: ResidentialHousingImport <nonprod|production> <preview|apply> <private-output-directory> [reviewed-preview-sha256]");
var environment = args[0]; var apply = args[1] == "apply"; var output = Path.GetFullPath(args[2]);
if (apply && args.Length != 4) throw new ArgumentException("Apply requires the exact reviewed preview hash.");
Directory.CreateDirectory(output);
var values = JsonSerializer.Deserialize<Dictionary<string, string>>(Environment.GetEnvironmentVariable("CF_HOUSING_IMPORT_SETTINGS")
    ?? throw new InvalidOperationException("Supply runtime settings securely through CF_HOUSING_IMPORT_SETTINGS."))!;
var pg = new NpgsqlConnectionStringBuilder(values["ConnectionStrings__Default"]);
if (pg.Host != "psql-campusflow-prod-001.postgres.database.azure.com" ||
    pg.Database != (environment == "production" ? "postgres" : "campusflow_nonprod") ||
    (environment == "nonprod") != (values.GetValueOrDefault("App__EnvironmentLabel") == "NON-PRODUCTION"))
    throw new InvalidOperationException("CampusFlow environment identity mismatch.");
var replica = values["ConnectionStrings__ThesisElementsReadOnly"];
if (!replica.Contains(environment == "production" ? "sql-elm-prod-usc-replica.database.windows.net" : "sql-elm-nonprod-usc-replica.database.windows.net", StringComparison.OrdinalIgnoreCase) ||
    !replica.Contains(environment == "production" ? "EL-NEL-DB" : "EL-NP-NEL-DB", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Elements environment identity mismatch.");
var cfg = new ConfigurationBuilder().AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key.Replace("__", ":"), x.Value))).Build();
var source = await new ElementsMigratedHousingReader(cfg).ReadForTermAsync(269)
    ?? throw new InvalidOperationException("The reviewed Spring 2027 source is unavailable.");
source.Validate();
await using var connection = new NpgsqlConnection(pg.ConnectionString); await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
await using (var settings = new NpgsqlCommand((apply ? "" : "SET TRANSACTION READ ONLY;") + "SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='45s';", connection, tx))
    await settings.ExecuteNonQueryAsync();
Guid id, tenant; string baseline, desired, extra, stamp; DateTime refreshed; object? modified; Guid? modifier;
await using (var query = new NpgsqlCommand("""
    SELECT d."Id",d."TenantId",d."BaselineJson",d."RoomsJson",d."ExtraProperties",d."ConcurrencyStamp",d."RefreshedAt",d."LastModificationTime",d."LastModifierId"
    FROM "AppHousingAssignmentDrafts" d JOIN "AbpTenants" t ON t."Id"=d."TenantId"
    WHERE d."PeriodId"=@Period AND NOT d."IsDeleted" AND NOT t."IsDeleted" AND lower(t."Name")='nelson'
    """ + (apply ? " FOR UPDATE OF d" : ""), connection, tx))
{
    query.Parameters.AddWithValue("Period", source.PeriodId);
    await using var row = await query.ExecuteReaderAsync();
    if (!await row.ReadAsync()) throw new InvalidOperationException("An existing Nelson housing workspace is required.");
    id = row.GetGuid(0); tenant = row.GetGuid(1); baseline = row.GetString(2); desired = row.GetString(3);
    extra = row.GetString(4); stamp = row.GetString(5); refreshed = row.GetDateTime(6); modified = row.IsDBNull(7) ? null : row.GetDateTime(7); modifier = row.IsDBNull(8) ? null : row.GetGuid(8);
    if (await row.ReadAsync()) throw new InvalidOperationException("Multiple housing workspaces need review.");
}
var properties = JsonNode.Parse(extra)!.AsObject();
if (properties.ContainsKey(ResidentialHousingImport.PropertyName)) throw new InvalidOperationException("This one-time import is already recorded. Review its existing evidence instead of importing again.");
var envelope = new HousingAssignmentDraft(id, tenant, source.PeriodId, baseline, refreshed);
foreach (var pair in properties) envelope.ExtraProperties[pair.Key] = pair.Value is null ? null! : JsonSerializer.Deserialize<JsonElement>(pair.Value.ToJsonString());
HousingApplyProgress.EnsureEditable(envelope, DateTime.UtcNow);
if (HousingRecoveryDispatch.Read(envelope)?.IsActive(envelope, DateTime.UtcNow) == true)
    throw new InvalidOperationException("A housing recovery is active. Import stopped.");
var baseRooms = JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(baseline)!;
var draftRooms = JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(desired)!;
var baseBefore = Capture(baseRooms); var draftBefore = Capture(draftRooms);
source.MergeInto(source.PeriodId, baseRooms); source.MergeInto(source.PeriodId, draftRooms);
Preserved(baseBefore, baseRooms); Preserved(draftBefore, draftRooms);
var nextBaseline = JsonSerializer.Serialize(baseRooms); var nextDesired = JsonSerializer.Serialize(draftRooms);
var previewHash = Hash(environment + "\n" + id + "\n" + tenant + "\n" + stamp + "\n" + baseline + "\n" + desired + "\n" + extra + "\n" + source.SourceSha256);
var evidence = new { environment, periodId = source.PeriodId, termCalendarId = source.TermCalendarId,
    sourceResidentialAssignments = source.Assignments.Count, baselineAssignmentsBefore = baseBefore.Count,
    baselineAssignmentsAfter = baseRooms.Sum(x => x.Occupants.Count), draftAssignmentsBefore = draftBefore.Count,
    draftAssignmentsAfter = draftRooms.Sum(x => x.Occupants.Count), existingAssignmentsPreserved = true,
    commuterAssignmentsPreserved = true, sourceSha256 = source.SourceSha256, previewSha256 = previewHash,
    elementsAssignmentsOrBillingWritten = false, scheduledJobCreated = false };
PrivateFile(Path.Combine(output, environment + "-source-" + previewHash + ".json"), JsonSerializer.Serialize(source), reuseIdentical: true);
if (apply)
{
    if (args[3] != previewHash) throw new InvalidOperationException("The source or workspace changed since preview. Nothing was imported.");
    source.ImportedAtUtc = DateTime.UtcNow;
    properties[ResidentialHousingImport.PropertyName] = JsonSerializer.Serialize(source);
    var nextExtra = properties.ToJsonString(); var nextStamp = Guid.NewGuid().ToString("N");
    PrivateFile(Path.Combine(output, environment + "-backup-" + previewHash + ".json"), JsonSerializer.Serialize(new {
        id, tenant, periodId=source.PeriodId, baseline, desired, extra, stamp, refreshed, modified, modifier, nextStamp, previewHash }));
    await using var update = new NpgsqlCommand("""
        UPDATE "AppHousingAssignmentDrafts" SET "BaselineJson"=@Baseline,"RoomsJson"=@Desired,"ExtraProperties"=@Extra,
            "ConcurrencyStamp"=@NextStamp,"LastModificationTime"=@Now,"LastModifierId"=NULL
        WHERE "Id"=@Id AND "TenantId"=@Tenant AND "ConcurrencyStamp"=@Stamp AND NOT "IsDeleted"
            AND "BaselineJson"=@OldBaseline AND "RoomsJson"=@OldDesired AND "ExtraProperties"=@OldExtra
        """, connection, tx);
    update.Parameters.AddWithValue("Baseline", nextBaseline); update.Parameters.AddWithValue("Desired", nextDesired);
    update.Parameters.AddWithValue("Extra", nextExtra); update.Parameters.AddWithValue("NextStamp", nextStamp);
    update.Parameters.AddWithValue("Now", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified));
    update.Parameters.AddWithValue("Id", id); update.Parameters.AddWithValue("Tenant", tenant); update.Parameters.AddWithValue("Stamp", stamp);
    update.Parameters.AddWithValue("OldBaseline", baseline); update.Parameters.AddWithValue("OldDesired", desired); update.Parameters.AddWithValue("OldExtra", extra);
    if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The housing workspace changed. Import was not committed.");
    await using var verify = new NpgsqlCommand("SELECT \"BaselineJson\",\"RoomsJson\",\"ExtraProperties\" FROM \"AppHousingAssignmentDrafts\" WHERE \"Id\"=@Id AND \"TenantId\"=@Tenant", connection, tx);
    verify.Parameters.AddWithValue("Id", id); verify.Parameters.AddWithValue("Tenant", tenant);
    await using (var row = await verify.ExecuteReaderAsync())
    {
        if (!await row.ReadAsync() || row.GetString(0) != nextBaseline || row.GetString(1) != nextDesired || row.GetString(2) != nextExtra)
            throw new InvalidOperationException("Import readback failed; transaction will roll back.");
    }
    await tx.CommitAsync();
}
else await tx.RollbackAsync();
var result = JsonSerializer.Serialize(new { mode = args[1], applied = apply, evidence, checkedAtUtc = DateTime.UtcNow });
PrivateFile(Path.Combine(output, environment + "-" + args[1] + "-" + previewHash + ".json"), result);
Console.WriteLine(result);

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
static Dictionary<int, string> Capture(List<HousingWorkspaceRoom> rooms) => rooms.SelectMany(room => room.Occupants.Select(o => (room.Id, Occupant:o)))
    .ToDictionary(x => x.Occupant.StudentUid, x => x.Id + ":" + JsonSerializer.Serialize(x.Occupant));
static void Preserved(Dictionary<int, string> before, List<HousingWorkspaceRoom> after)
{
    var captured = Capture(after);
    if (before.Any(x => !captured.TryGetValue(x.Key, out var value) || value != x.Value))
        throw new InvalidOperationException("An existing assignment or its metadata would change. Import stopped.");
}
static void PrivateFile(string path, string contents, bool reuseIdentical = false)
{
    if (reuseIdentical && File.Exists(path) && File.ReadAllText(path) == contents) return;
    // Backups contain student names and room information. Never overwrite an earlier backup.
    using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
    using var writer = new StreamWriter(stream); writer.Write(contents);
}
