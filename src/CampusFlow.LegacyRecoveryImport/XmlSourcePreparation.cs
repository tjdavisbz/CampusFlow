using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace CampusFlow.LegacyRecoveryImport;

internal sealed class XmlPreparationOptions
{
    public string Environment { get; private init; } = "";
    public string LegacyServer { get; private init; } = "";
    public string LegacyDatabase { get; private init; } = "";
    public string Input { get; private init; } = "";
    public string ExpectedSha256 { get; private init; } = "";
    public string Output { get; private init; } = "";
    public string? StudentUpn { get; private init; }
    public bool All { get; private init; }
    public string? ReuseManifest { get; private init; }
    public string? ReuseSha256 { get; private init; }
    public static XmlPreparationOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "prepare-xml") throw new ImportFailure("InvalidArguments");
        var accepted = new HashSet<string>(["--environment", "--expected-legacy-server", "--expected-legacy-database", "--input", "--expected-sha256", "--output", "--student-upn", "--reuse-manifest", "--reuse-sha256"], StringComparer.Ordinal);
        var values = new Dictionary<string, string?>();
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (values.ContainsKey(key)) throw new ImportFailure("InvalidArguments");
            if (key == "--all") { values.Add(key, null); continue; }
            if (!accepted.Contains(key) || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ImportFailure("InvalidArguments");
            values.Add(key, args[i]);
        }
        string Required(string key) => values.GetValueOrDefault(key) is { Length: > 0 } value ? value : throw new ImportFailure("InvalidArguments");
        var environment = Required("--environment");
        if (environment is not ("nonprod" or "production")) throw new ImportFailure("InvalidEnvironment");
        var all = values.ContainsKey("--all"); var upn = values.GetValueOrDefault("--student-upn");
        if (all == (upn is not null) || environment == "nonprod" && all) throw new ImportFailure("ExplicitSingleStudentRequiredForNonprodXml");
        if (upn is not null)
        {
            if (!MailAddress.TryCreate(upn, out var mail) || !mail.Host.Equals("my.nelson.edu", StringComparison.OrdinalIgnoreCase) ||
                mail.DisplayName.Length != 0 || !mail.Address.Equals(upn, StringComparison.OrdinalIgnoreCase) || upn.Any(char.IsWhiteSpace))
                throw new ImportFailure("InvalidStudentUpn");
        }
        var reuse = values.GetValueOrDefault("--reuse-manifest"); var reuseHash = values.GetValueOrDefault("--reuse-sha256");
        if ((reuse is null) != (reuseHash is null) || reuse is not null && (string.IsNullOrWhiteSpace(reuse) || !ImportModel.IsHash(reuseHash)))
            throw new ImportFailure("InvalidReuseArguments");
        var hash = Required("--expected-sha256");
        if (!ImportModel.IsHash(hash)) throw new ImportFailure("InvalidManifestHash");
        return new() { Environment = environment, LegacyServer = Required("--expected-legacy-server"), LegacyDatabase = Required("--expected-legacy-database"),
            Input = Required("--input"), Output = Required("--output"), ExpectedSha256 = hash, StudentUpn = upn, All = all,
            ReuseManifest = reuse, ReuseSha256 = reuseHash };
    }
}

internal sealed record XmlLegacyStudent(StudentIdentifier Student, IReadOnlyList<LegacyQuestion> Questions, bool NoQuestionAllowed)
{ public override string ToString() => "Legacy XML verification [redacted]"; }
internal sealed record XmlLegacySnapshot(string Server, string Database, string DatabaseCollation, string AnswerCollation, IReadOnlyList<XmlLegacyStudent> Students)
{ public override string ToString() => "Legacy XML snapshot [private]"; }

internal static class XmlSourcePreparation
{
    internal static XmlLegacySnapshot Read(byte[] bytes, string expectedSha256, string expectedServer, string expectedDatabase)
    {
        try
        {
            if (bytes.Length is < 1 or > 134217728 || !ImportModel.IsHash(expectedSha256) ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedSha256))) throw new ImportFailure("SourceXmlHashMismatch");
            using var textReader = new StreamReader(new MemoryStream(bytes, false), new UTF8Encoding(false, true), true);
            var text = textReader.ReadToEnd();
            var start = text.IndexOf('<');
            if (start < 0 || text[..start].Trim() is not ("" or "(No column name)")) throw new ImportFailure("InvalidSourceXmlHeader");
            using var reader = XmlReader.Create(new StringReader(text[start..]), new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 134217728, MaxCharactersFromEntities = 0 });
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw new ImportFailure("InvalidSourceXml");
            if (root.Name != "CampusFlowRecoveryExport" || root.Attributes().Count() != 2 ||
                (string?)root.Attribute("FormatVersion") != "1" || (string?)root.Attribute("Kind") != "CampusFlow.LegacyRecoverySourceXml") throw new ImportFailure("InvalidSourceXmlFormat");
            Shape(root, ["LegacyServer", "LegacyDatabase", "ExportedAtUtc", "DatabaseCollation", "AnswerCollation", "FerpaFunctionAvailable", "ExpectedStudentCount", "Selection", "Students"], allowAttributes: true);
            var server = Value(root, "LegacyServer"); var database = Value(root, "LegacyDatabase");
            var dbCollation = Value(root, "DatabaseCollation"); var answerCollation = Value(root, "AnswerCollation");
            if (!server.Equals(expectedServer, StringComparison.OrdinalIgnoreCase) || !database.Equals(expectedDatabase, StringComparison.OrdinalIgnoreCase) ||
                !ImportModel.SupportedAnswerCollation(dbCollation) || !ImportModel.SupportedAnswerCollation(answerCollation)) throw new ImportFailure("UnreviewedSourceXmlContext");
            if (!DateTime.TryParse(Value(root, "ExportedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exported) ||
                exported > DateTime.UtcNow.AddMinutes(5) || Value(root, "Selection") is not ("all" or "single-student")) throw new ImportFailure("InvalidSourceXmlMetadata");
            var ferpaAvailable = Bit(Value(root, "FerpaFunctionAvailable"));
            var expected = Number(Value(root, "ExpectedStudentCount"), 1, 100000);
            var container = root.Element("Students")!;
            if (container.HasAttributes || container.Elements().Any(x => x.Name != "Student") || container.Elements().Count() != expected) throw new ImportFailure("SourceXmlCountMismatch");
            var students = new List<XmlLegacyStudent>(); var ids = new HashSet<int>();
            foreach (var student in container.Elements())
            {
                Shape(student, ["LegacyStudentUid", "StudentId", "StoredQuestionCount", "LegacyNoQuestionAllowed"], ["Questions"]);
                var uid = Number(Value(student, "LegacyStudentUid"), 1, int.MaxValue);
                if (!ids.Add(uid)) throw new ImportFailure("DuplicateLegacyXmlUid");
                var id = ImportModel.StudentId(Value(student, "StudentId"));
                var saved = Number(Value(student, "StoredQuestionCount"), 0, int.MaxValue);
                var allowed = Bit(Value(student, "LegacyNoQuestionAllowed"));
                if (allowed && (!ferpaAvailable || saved != 0)) throw new ImportFailure("InconsistentLegacyXmlEligibility");
                var qs = student.Element("Questions") ?? new XElement("Questions");
                if (qs.HasAttributes || qs.Elements().Any(x => x.Name != "Question") || qs.Elements().Count() != Math.Min(saved, 2)) throw new ImportFailure("InconsistentLegacyXmlQuestions");
                var questions = new List<LegacyQuestion>();
                foreach (var q in qs.Elements())
                {
                    Shape(q, [], ["QuestionId", "Text", "Answer"]);
                    var qid = q.Element("QuestionId")?.Value;
                    questions.Add(new(qid is null ? null : Number(qid, 0, int.MaxValue), q.Element("Text")?.Value, q.Element("Answer")?.Value));
                }
                students.Add(new(new(uid, id), questions, allowed));
            }
            return new(server, database, dbCollation, answerCollation, students);
        }
        catch (ImportFailure) { throw; }
        catch (Exception) { throw new ImportFailure("InvalidSourceXml"); }
    }
    private static void Shape(XElement element, string[] required, string[]? optional = null, bool allowAttributes = false)
    {
        var allowed = required.Concat(optional ?? []).ToHashSet(StringComparer.Ordinal);
        if (!allowAttributes && element.HasAttributes || element.Elements().Any(x => !allowed.Contains(x.Name.ToString())) ||
            required.Any(name => element.Elements(name).Count() != 1) ||
            element.Elements().GroupBy(x => x.Name).Any(g => g.Count() > 1)) throw new ImportFailure("InvalidSourceXmlShape");
        foreach (var child in element.Elements().Where(x => x.Name != "Students" && x.Name != "Questions"))
            if (child.HasAttributes || child.HasElements) throw new ImportFailure("InvalidSourceXmlValue");
    }
    private static string Value(XElement element, string name) => element.Element(name)!.Value;
    private static bool Bit(string value) => value switch { "1" or "true" => true, "0" or "false" => false, _ => throw new ImportFailure("InvalidSourceXmlBoolean") };
    private static int Number(string value, int min, int max) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
        ? number : throw new ImportFailure("InvalidSourceXmlNumber");

    internal static RecoveryManifest Prepare(XmlLegacySnapshot source, string environment,
        IReadOnlyList<StudentIdentifier> current, HashSet<int> eligible, int? selectedUid, RecoveryManifest? previous = null)
    {
        var target = ImportModel.ElementsTarget(environment);
        if (environment == "nonprod" && selectedUid is null) throw new ImportFailure("ExplicitSingleStudentRequiredForNonprodXml");
        var manifest = new RecoveryManifest { Environment = environment, CreatedAtUtc = DateTime.UtcNow,
            LegacyServer = source.Server, LegacyDatabase = source.Database, LegacyDatabaseCollation = source.DatabaseCollation,
            LegacyAnswerCollation = source.AnswerCollation, ElementsServer = target.Server, ElementsDatabase = target.Database,
            Selection = selectedUid.HasValue ? "single-student" : "all-explicit" };
        var reuse = previous is null ? null : ReusableManifest.FromValidated(previous, manifest);
        var index = current.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var matches = new List<(StudentIdentifier Current, XmlLegacyStudent Legacy)>();
        foreach (var group in source.Students.GroupBy(x => x.Student.Id, StringComparer.Ordinal))
        {
            if (!index.TryGetValue(group.Key, out var rows) || rows.Length != 1 || !eligible.Contains(rows[0].Uid))
            { if (selectedUid is null) manifest.MissingOrAmbiguousMappings++; continue; }
            if (selectedUid is int selected && rows[0].Uid != selected) continue;
            if (group.Count() != 1) { manifest.MissingOrAmbiguousMappings++; continue; }
            matches.Add((rows[0], group.Single()));
        }
        if (matches.Count is < 1 or > 100000 || matches.Select(x => x.Current.Uid).Distinct().Count() != matches.Count) throw new ImportFailure("NoUniqueMappedXmlStudents");
        var records = new RecoveryImportRecord[matches.Count];
        // Bound CPU use while hashing; ordering is restored independently of worker completion.
        Parallel.For(0, matches.Count, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i => {
            var item = matches[i]; records[i] = ImportModel.Record(manifest, item.Current, item.Legacy.Student,
                item.Legacy.Questions, item.Legacy.NoQuestionAllowed, reuse?.Find(item.Current.Uid));
        });
        manifest.Records = records.OrderBy(x => x.StudentUid).ToList(); reuse?.EnsurePreserved(manifest);
        return manifest;
    }

    internal static async Task<object> RunAsync(XmlPreparationOptions options)
    {
        _ = PrivateManifestFile.OutputPath(options.Output);
        var info = new FileInfo(options.Input);
        if (info.Length is < 1 or > 134217728 || info.LinkTarget is not null) throw new ImportFailure("InvalidSourceXmlFile");
        var bytes = await File.ReadAllBytesAsync(options.Input); XmlLegacySnapshot source;
        try { source = Read(bytes, options.ExpectedSha256, options.LegacyServer, options.LegacyDatabase); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var target = ImportModel.ElementsTarget(options.Environment);
        var connection = ImportModel.SqlConnection(SqlSources.RequiredEnvironment("CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING"), target.Server, target.Database);
        await using var database = new SqlConnection(connection); await database.OpenAsync();
        var current = await SqlSources.StudentsAsync(database, null, true); int? selectedUid = null;
        if (options.StudentUpn is not null)
        {
            using var select = new SqlCommand("SELECT TOP (2) address.StudentUID FROM dbo.CAMS_StudentAddressList_View address WHERE address.ActiveFlag='Yes' AND address.AddressType='Local' AND LOWER(LTRIM(RTRIM(address.Email1)))=LOWER(@Upn)", database) { CommandTimeout = 10 };
            select.Parameters.Add("@Upn", System.Data.SqlDbType.NVarChar, 254).Value = options.StudentUpn;
            await using var reader = await select.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new ImportFailure("TestStudentMappingMissing");
            selectedUid = reader.GetInt32(0);
            if (await reader.ReadAsync() || !current.Eligible.Contains(selectedUid.Value)) throw new ImportFailure("TestStudentMappingAmbiguous");
        }
        RecoveryManifest? previous = null;
        if (options.ReuseManifest is not null)
        {
            var priorBytes = PrivateManifestFile.Read(options.ReuseManifest, options.ReuseSha256!);
            try { previous = JsonSerializer.Deserialize<RecoveryManifest>(priorBytes, ImportModel.Json) ?? throw new ImportFailure("InvalidReuseManifest"); }
            finally { CryptographicOperations.ZeroMemory(priorBytes); }
            var priorOptions = ImportOptions.Parse(["export", "--environment", options.Environment, "--expected-legacy-server", options.LegacyServer,
                "--expected-legacy-database", options.LegacyDatabase, "--all", "--output", options.Output]);
            ImportModel.ValidateManifest(previous, priorOptions);
        }
        var manifest = Prepare(source, options.Environment, current.Students, current.Eligible, selectedUid, previous);
        var outputBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportModel.Json);
        try
        {
            var hash = PrivateManifestFile.Write(options.Output, outputBytes);
            return new { action = "prepare-xml", environment = options.Environment, legacyStudents = source.Students.Count,
                records = manifest.Records.Count, blocked = manifest.Records.Count(x => x.IsBlocked),
                questionProofs = manifest.Records.Count(x => x.AnswerHash is not null),
                noQuestionProofs = manifest.Records.Count(x => !x.IsBlocked && x.LegacyNoQuestionAllowed),
                skippedMappings = manifest.MissingOrAmbiguousMappings, sha256 = hash, privateFileCreated = true,
                plaintextAnswersPersisted = false, databaseWrites = false, runtimeEnabled = false, passwordsChanged = false };
        }
        finally { CryptographicOperations.ZeroMemory(outputBytes); }
    }
}
