using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFlow.AccountRecovery;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CampusFlow.LegacyRecoveryImport;

internal sealed class ImportFailure(string code) : Exception("Recovery import stopped: " + code)
{ public string Code { get; } = code; }

internal sealed class ImportOptions
{
    public string Mode { get; private init; } = "";
    public string Environment { get; private init; } = "";
    public string LegacyServer { get; private init; } = "";
    public string LegacyDatabase { get; private init; } = "";
    public string? StudentId { get; private init; }
    public bool All { get; private init; }
    public bool Apply { get; private init; }
    public string File { get; private init; } = "";
    public string? ExpectedSha256 { get; private init; }
    public int? ExpectedRows { get; private init; }
    public string? ReuseManifest { get; private init; }
    public string? ReuseSha256 { get; private init; }
    public static ImportOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("export" or "apply")) throw new ImportFailure("InvalidArguments");
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var switches = new HashSet<string>(["--all", "--apply"], StringComparer.Ordinal);
        var allowed = new HashSet<string>(["--environment", "--expected-legacy-server", "--expected-legacy-database", "--student-id", "--output", "--input", "--expected-sha256", "--expected-rows", "--reuse-manifest", "--reuse-sha256"], StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (options.ContainsKey(key) || !switches.Contains(key) && !allowed.Contains(key)) throw new ImportFailure("InvalidArguments");
            if (switches.Contains(key)) options.Add(key, null);
            else
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ImportFailure("InvalidArguments");
                options.Add(key, args[i]);
            }
        }
        string Required(string key) => options.GetValueOrDefault(key) is { Length: > 0 } value ? value : throw new ImportFailure("InvalidArguments");
        var mode = args[0]; var environment = Required("--environment");
        if (environment is not ("nonprod" or "production")) throw new ImportFailure("InvalidEnvironment");
        var all = options.ContainsKey("--all"); var student = options.GetValueOrDefault("--student-id");
        if (mode == "export" && (all == (student is not null) || options.ContainsKey("--apply") || options.ContainsKey("--input") || options.ContainsKey("--expected-sha256") || options.ContainsKey("--expected-rows"))) throw new ImportFailure("InvalidArguments");
        if (mode == "apply" && (all || student is not null || options.ContainsKey("--output"))) throw new ImportFailure("InvalidArguments");
        var reuse = options.GetValueOrDefault("--reuse-manifest"); var reuseHash = options.GetValueOrDefault("--reuse-sha256");
        if ((reuse is null) != (reuseHash is null) || mode != "export" && reuse is not null ||
            reuse is not null && (string.IsNullOrWhiteSpace(reuse) || !ImportModel.IsHash(reuseHash)))
            throw new ImportFailure("InvalidReuseArguments");
        var fingerprint = mode == "apply" ? Required("--expected-sha256").ToLowerInvariant() : null;
        if (fingerprint is not null && !ImportModel.IsHash(fingerprint)) throw new ImportFailure("InvalidManifestHash");
        int? expectedRows = null;
        if (mode == "apply")
        {
            if (!int.TryParse(Required("--expected-rows"), out var count) || count is < 1 or > 100000) throw new ImportFailure("InvalidArguments");
            expectedRows = count;
        }
        return new ImportOptions { Mode = mode, Environment = environment,
            LegacyServer = Required("--expected-legacy-server"), LegacyDatabase = Required("--expected-legacy-database"),
            StudentId = student is null ? null : ImportModel.StudentId(student), All = all, Apply = options.ContainsKey("--apply"),
            File = Required(mode == "export" ? "--output" : "--input"), ExpectedSha256 = fingerprint, ExpectedRows = expectedRows,
            ReuseManifest = reuse, ReuseSha256 = reuseHash?.ToLowerInvariant() };
    }
}

internal sealed class RecoveryManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Environment { get; set; } = "";
    public string TenantName { get; set; } = "nelson";
    public DateTime CreatedAtUtc { get; set; }
    public string LegacyServer { get; set; } = "";
    public string LegacyDatabase { get; set; } = "";
    public string LegacyDatabaseCollation { get; set; } = "";
    public string LegacyAnswerCollation { get; set; } = "";
    public string AnswerNormalization { get; set; } = "Trim-FormC-UpperInvariant-v1";
    public string ElementsServer { get; set; } = "";
    public string ElementsDatabase { get; set; } = "";
    public string Selection { get; set; } = "";
    public int MissingOrAmbiguousMappings { get; set; }
    public List<RecoveryImportRecord> Records { get; set; } = [];
    public override string ToString() => "Recovery verification manifest [private]";
}

internal sealed class RecoveryImportRecord
{
    public int StudentUid { get; set; }
    public int LegacyStudentUid { get; set; }
    public string StudentIdHash { get; set; } = "";
    public int? QuestionId { get; set; }
    public string? Question { get; set; }
    public string? AnswerHash { get; set; }
    public bool LegacyNoQuestionAllowed { get; set; }
    public bool IsBlocked { get; set; }
    public string SourceFingerprint { get; set; } = "";
    public override string ToString() => "Recovery verification record [private]";
}

internal sealed record StudentIdentifier(int Uid, string Id)
{ public override string ToString() => "Student identifier [redacted]"; }
internal sealed record LegacyQuestion(int? Id, string? Text, string? Answer)
{ public override string ToString() => "Legacy verification question [redacted]"; }

internal static class ImportModel
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true
    };
    internal static bool IsHash(string? value) => value?.Length == 64 && value.All(char.IsAsciiHexDigit);
    internal static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    internal static string StudentId(string input)
    {
        var value = input.Trim();
        if (value.Length is < 1 or > 50 || value.Any(char.IsControl)) throw new ImportFailure("InvalidStudentIdentifier");
        return value;
    }
    internal static string StudentIdHash(string value) => Sha(Encoding.UTF8.GetBytes(StudentId(value)));
    internal static (string Server, string Database) ElementsTarget(string environment) => environment switch
    {
        "nonprod" => ("sql-elm-nonprod-usc-replica.database.windows.net", "EL-NP-NEL-DB"),
        "production" => ("sql-elm-prod-usc-replica.database.windows.net", "EL-NEL-DB"),
        _ => throw new ImportFailure("InvalidEnvironment")
    };
    internal static string SqlConnection(string? value, string server, string database)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) ||
            server.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('.' or '-' or '_')) || database.Length > 128 || database.Any(char.IsControl))
            throw new ImportFailure("InvalidSourceConfiguration");
        var b = new SqlConnectionStringBuilder(value);
        if (!(b.DataSource.Equals(server, StringComparison.OrdinalIgnoreCase) || b.DataSource.Equals("tcp:" + server + ",1433", StringComparison.OrdinalIgnoreCase)) ||
            !b.InitialCatalog.Equals(database, StringComparison.OrdinalIgnoreCase) || b.TrustServerCertificate || b.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new ImportFailure("WrongOrUntrustedSource");
        b.ApplicationIntent = ApplicationIntent.ReadOnly; b.PersistSecurityInfo = false; b.ConnectRetryCount = 0;
        b.ConnectTimeout = b.ConnectTimeout is > 0 and < 10 ? b.ConnectTimeout : 10;
        return b.ConnectionString;
    }
    internal static string PortalConnection(string? value, string environment)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ImportFailure("PortalConnectionMissing");
        var b = new NpgsqlConnectionStringBuilder(value);
        if (!string.Equals(b.Host, "psql-campusflow-prod-001.postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) ||
            b.Database != (environment == "nonprod" ? "campusflow_nonprod" : environment == "production" ? "postgres" : throw new ImportFailure("InvalidEnvironment")) || b.Port != 5432)
            throw new ImportFailure("WrongPortalEnvironment");
        b.SslMode = SslMode.VerifyFull; b.PersistSecurityInfo = false; b.Timeout = 10; b.CommandTimeout = 30;
        b.IncludeErrorDetail = false; b.ApplicationName = "CampusFlow recovery verification import";
        return b.ConnectionString;
    }
    internal static bool SupportedAnswerCollation(string? collation) => collation is not null && new[] {
        "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_CI_AS", "Latin1_General_100_CI_AS", "Latin1_General_100_CI_AS_SC"
    }.Contains(collation, StringComparer.OrdinalIgnoreCase);
    internal static string Fingerprint(RecoveryManifest manifest, RecoveryImportRecord record)
    {
        var canonical = new { manifest.FormatVersion, manifest.Environment, manifest.TenantName, manifest.LegacyServer,
            manifest.LegacyDatabase, manifest.LegacyDatabaseCollation, manifest.LegacyAnswerCollation, manifest.AnswerNormalization, manifest.ElementsServer, manifest.ElementsDatabase, record.StudentUid,
            record.LegacyStudentUid, record.StudentIdHash, record.QuestionId, record.Question, record.AnswerHash,
            record.LegacyNoQuestionAllowed, record.IsBlocked };
        return Sha(JsonSerializer.SerializeToUtf8Bytes(canonical));
    }
    internal static RecoveryImportRecord Record(RecoveryManifest manifest, StudentIdentifier current, StudentIdentifier legacy,
        IReadOnlyList<LegacyQuestion> questions, bool? noQuestionAllowed, RecoveryImportRecord? previous = null)
    {
        if (current.Uid <= 0 || legacy.Uid <= 0 || StudentId(current.Id) != StudentId(legacy.Id)) throw new ImportFailure("AmbiguousStudentMapping");
        var record = new RecoveryImportRecord { StudentUid = current.Uid, LegacyStudentUid = legacy.Uid, StudentIdHash = StudentIdHash(current.Id), IsBlocked = true };
        if (questions.Count == 0 && noQuestionAllowed == true)
        { record.IsBlocked = false; record.LegacyNoQuestionAllowed = true; }
        else if (questions.Count == 1 && questions[0] is { Id: > 0, Text: not null, Answer: not null } question &&
            !string.IsNullOrWhiteSpace(question.Text) && question.Text.Length <= 500 && !string.IsNullOrWhiteSpace(question.Answer) && question.Answer.Length <= 500)
        {
            record.QuestionId = question.Id; record.Question = question.Text;
            if (previous is not null && !RecoveryAnswerHash.Verify(question.Answer, previous.AnswerHash))
                throw new ImportFailure("PriorVerificationChangedRequiresReview");
            record.AnswerHash = previous?.AnswerHash ?? RecoveryAnswerHash.Create(question.Answer); record.IsBlocked = false;
        }
        record.SourceFingerprint = Fingerprint(manifest, record); ValidateRecord(manifest, record);
        if (previous is not null && (record.StudentUid != previous.StudentUid || record.StudentIdHash != previous.StudentIdHash ||
            !PortalImport.ExistingMatches(record, previous.LegacyStudentUid, previous.QuestionId, previous.Question,
                previous.AnswerHash, previous.LegacyNoQuestionAllowed, previous.IsBlocked, previous.SourceFingerprint)))
            throw new ImportFailure("PriorVerificationChangedRequiresReview");
        return record;
    }
    internal static void ValidateRecord(RecoveryManifest manifest, RecoveryImportRecord record)
    {
        if (!IsHash(record.StudentIdHash) || !IsHash(record.SourceFingerprint) || Fingerprint(manifest, record) != record.SourceFingerprint)
            throw new ImportFailure("InvalidRecordFingerprint");
        if (record.IsBlocked && (record.QuestionId is not null || record.Question is not null || record.AnswerHash is not null || record.LegacyNoQuestionAllowed))
            throw new ImportFailure("BlockedProofMustNotCarryAnswerData");
        _ = new RecoveryVerification(Guid.NewGuid(), Guid.NewGuid(), record.StudentUid, record.LegacyStudentUid,
            record.QuestionId, record.Question, record.AnswerHash, record.LegacyNoQuestionAllowed, record.IsBlocked, record.SourceFingerprint);
    }
    internal static void ValidateManifest(RecoveryManifest manifest, ImportOptions options)
    {
        var expected = ElementsTarget(options.Environment);
        if (manifest.Records is null || manifest.LegacyServer is null || manifest.LegacyDatabase is null || manifest.ElementsServer is null || !SupportedAnswerCollation(manifest.LegacyDatabaseCollation) || !SupportedAnswerCollation(manifest.LegacyAnswerCollation) || manifest.AnswerNormalization != "Trim-FormC-UpperInvariant-v1" || manifest.FormatVersion != 1 || manifest.Environment != options.Environment || manifest.TenantName != "nelson" ||
            !manifest.LegacyServer.Equals(options.LegacyServer, StringComparison.OrdinalIgnoreCase) ||
            !manifest.LegacyDatabase.Equals(options.LegacyDatabase, StringComparison.OrdinalIgnoreCase) ||
            !manifest.ElementsServer.Equals(expected.Server, StringComparison.OrdinalIgnoreCase) || manifest.ElementsDatabase != expected.Database ||
            manifest.CreatedAtUtc.Kind != DateTimeKind.Utc || manifest.CreatedAtUtc > DateTime.UtcNow.AddMinutes(5) ||
            manifest.Records.Count is < 1 or > 100000 || manifest.Records.Select(x => x.StudentUid).Distinct().Count() != manifest.Records.Count ||
            manifest.Records.Select(x => x.LegacyStudentUid).Distinct().Count() != manifest.Records.Count ||
            manifest.Records.Select(x => x.StudentIdHash).Distinct().Count() != manifest.Records.Count ||
            options.Mode == "apply" && manifest.Records.Count != options.ExpectedRows)
            throw new ImportFailure("WrongOrAmbiguousManifest");
        foreach (var row in manifest.Records) ValidateRecord(manifest, row);
    }
}
