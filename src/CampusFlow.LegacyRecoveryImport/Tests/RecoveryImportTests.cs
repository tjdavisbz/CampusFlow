using System.Text;
using System.Text.Json;
using CampusFlow.AccountRecovery;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace CampusFlow.LegacyRecoveryImport.Tests;

public sealed class RecoveryImportTests
{
    private static RecoveryManifest Manifest() => new() { Environment = "nonprod", TenantName = "nelson", CreatedAtUtc = DateTime.UtcNow,
        LegacyServer = "windb3", LegacyDatabase = "CAMS_Enterprise", LegacyDatabaseCollation = "SQL_Latin1_General_CP1_CI_AS", LegacyAnswerCollation = "SQL_Latin1_General_CP1_CI_AS", ElementsServer = "sql-elm-nonprod-usc-replica.database.windows.net", ElementsDatabase = "EL-NP-NEL-DB", Selection = "single-student" };
    private static RecoveryImportRecord Row(RecoveryManifest manifest, int uid = 42, int legacy = 999, string id = "00001234") =>
        ImportModel.Record(manifest, new(uid, id), new(legacy, id), [new(7, "A synthetic question?", "Synthetic private answer")], false);
    private static string[] ApplyArgs(string? input = null) => ["apply", "--environment", "nonprod", "--expected-legacy-server", "windb3", "--expected-legacy-database", "CAMS_Enterprise", "--input", input ?? "/private/tmp/private-synthetic.json", "--expected-sha256", new string('a', 64), "--expected-rows", "1"];

    private static string[] ExportArgs() => ["export", "--environment", "nonprod", "--expected-legacy-server", "windb3", "--expected-legacy-database", "CAMS_Enterprise", "--all", "--output", "/private/tmp/private-synthetic.json"];

    [Fact] public void Export_requires_one_explicit_student_or_an_explicit_bulk_switch()
    {
        var args = new[] { "export", "--environment", "nonprod", "--expected-legacy-server", "windb3", "--expected-legacy-database", "CAMS_Enterprise", "--output", "/private/tmp/private-synthetic.json" };
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse(args));
        var single = ImportOptions.Parse([.. args, "--student-id", "00001234"]); Assert.Equal("00001234", single.StudentId); Assert.False(single.All);
        Assert.True(ImportOptions.Parse([.. args, "--all"]).All);
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. args, "--student-id", "00001234", "--all"]));
    }
    [Fact] public void Apply_defaults_to_preview_and_requires_reviewed_hash_and_count()
    {
        Assert.False(ImportOptions.Parse(ApplyArgs()).Apply);
        Assert.True(ImportOptions.Parse([.. ApplyArgs(), "--apply"]).Apply);
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse(ApplyArgs()[..^2]));
    }
    [Theory]
    [InlineData("--connection-string")] [InlineData("--password")] [InlineData("--configuration")]
    public void Credential_arguments_are_not_accepted_or_echoed(string key)
    {
        var failure = Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ApplyArgs(), key, "SYNTHETIC-DO-NOT-PRINT"]));
        Assert.DoesNotContain("SYNTHETIC-DO-NOT-PRINT", failure.Message);
    }
    [Fact] public void Duplicate_flags_and_mixed_apply_export_options_fail_closed()
    {
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ApplyArgs(), "--apply", "--apply"]));
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ApplyArgs(), "--all"]));
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ApplyArgs(), "--output", "ignored"]));
    }
    [Fact] public void Current_and_legacy_IDs_are_independently_mapped_by_StudentID()
    {
        var manifest = Manifest(); var record = Row(manifest);
        Assert.Equal(42, record.StudentUid); Assert.Equal(999, record.LegacyStudentUid);
        Assert.Equal(ImportModel.StudentIdHash("00001234"), record.StudentIdHash);
        Assert.NotEqual(ImportModel.StudentIdHash("1234"), record.StudentIdHash);
        Assert.Throws<ImportFailure>(() => ImportModel.Record(manifest, new(42, "1234"), new(999, "00001234"), [], true));
    }
    [Fact] public void Export_record_contains_a_salted_hash_but_no_raw_answer_SSN_DOB_or_portal_password()
    {
        var manifest = Manifest(); var record = Row(manifest); manifest.Records.Add(record);
        var json = JsonSerializer.Serialize(manifest, ImportModel.Json);
        Assert.DoesNotContain("Synthetic private answer", json);
        Assert.DoesNotContain("\"answer\":", json); Assert.DoesNotContain("ssn", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("birth", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("portalPassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(RecoveryAnswerHash.Verify("Synthetic private answer", record.AnswerHash));
        Assert.NotEqual(record.AnswerHash, Row(manifest).AnswerHash);
    }
    [Theory] [InlineData(null)] [InlineData(false)]
    public void A_zero_question_record_is_blocked_without_explicit_trusted_eligibility(bool? permitted)
    {
        var row = ImportModel.Record(Manifest(), new(42, "1234"), new(999, "1234"), [], permitted);
        Assert.True(row.IsBlocked); Assert.False(row.LegacyNoQuestionAllowed); Assert.Null(row.AnswerHash);
    }
    [Fact] public void A_verified_zero_question_predicate_is_the_only_no_question_path()
    {
        var row = ImportModel.Record(Manifest(), new(42, "1234"), new(999, "1234"), [], true);
        Assert.False(row.IsBlocked); Assert.True(row.LegacyNoQuestionAllowed); Assert.Null(row.AnswerHash);
    }
    [Fact] public void Missing_answers_and_duplicate_question_rows_produce_only_blocked_proof()
    {
        foreach (var questions in new IReadOnlyList<LegacyQuestion>[] { [new(7, "Question?", null)], [new(7, "Question?", "")],
            [new(7, null, "Answer")], [new(7, "Question?", "Answer"), new(7, "Question?", "Answer")] })
        {
            var row = ImportModel.Record(Manifest(), new(42, "1234"), new(999, "1234"), questions, true);
            Assert.True(row.IsBlocked); Assert.False(row.LegacyNoQuestionAllowed);
            Assert.Null(row.QuestionId); Assert.Null(row.Question); Assert.Null(row.AnswerHash);
        }
    }
    [Fact] public void Legacy_queries_parameterize_student_UID_and_export_only_the_FERPA_boolean()
    {
        using var q = SqlSources.QuestionsCommand(null, 999);
        Assert.Contains("TOP (2)", q.CommandText); Assert.Contains("LEFT JOIN", q.CommandText);
        Assert.Single(q.Parameters.Cast<SqlParameter>()); Assert.Equal(999, q.Parameters["@LegacyStudentUid"].Value);
        using var predicate = SqlSources.NoQuestionCommand(null, 999);
        Assert.Equal("SELECT CASE WHEN dbo.FERPA(@LegacyStudentUid) <> -9999999 THEN 1 ELSE 0 END;", predicate.CommandText);
        Assert.Single(predicate.Parameters.Cast<SqlParameter>());
    }
    [Theory]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", true)] [InlineData("Latin1_General_100_CI_AS_SC", true)]
    [InlineData("SQL_Latin1_General_CP1_CS_AS", false)] [InlineData("Latin1_General_CI_AI", false)]
    [InlineData("Turkish_CI_AS", false)] [InlineData(null, false)]
    public void Answer_collation_must_be_an_explicit_reviewed_case_insensitive_accent_sensitive_choice(string? collation, bool allowed)
    { Assert.Equal(allowed, ImportModel.SupportedAnswerCollation(collation)); }
    [Fact] public void Canonical_fingerprint_detects_mutation_of_mapping_or_verification_flags()
    {
        var manifest = Manifest(); var row = Row(manifest); ImportModel.ValidateRecord(manifest, row);
        row.LegacyNoQuestionAllowed = true; Assert.Throws<ImportFailure>(() => ImportModel.ValidateRecord(manifest, row));
    }
    [Fact] public void A_blocked_record_cannot_smuggle_plaintext_into_an_answer_hash_field()
    {
        var manifest = Manifest(); var row = ImportModel.Record(manifest, new(42, "1234"), new(999, "1234"), [], false);
        row.AnswerHash = "Never store this"; row.SourceFingerprint = ImportModel.Fingerprint(manifest, row);
        Assert.Throws<ImportFailure>(() => ImportModel.ValidateRecord(manifest, row));
    }
    [Fact] public void Manifest_binds_environment_target_schema_selection_and_unique_mappings()
    {
        var manifest = Manifest(); manifest.Records.Add(Row(manifest)); var options = ImportOptions.Parse(ApplyArgs());
        ImportModel.ValidateManifest(manifest, options);
        manifest.Environment = "production"; Assert.Throws<ImportFailure>(() => ImportModel.ValidateManifest(manifest, options));
        manifest.Environment = "nonprod"; manifest.Records.Add(Row(manifest)); Assert.Throws<ImportFailure>(() => ImportModel.ValidateManifest(manifest, options));
    }
    [Fact] public void Existing_proofs_must_match_every_preserved_field_to_be_unchanged()
    {
        var manifest = Manifest(); var row = Row(manifest);
        Assert.True(PortalImport.ExistingMatches(row, row.LegacyStudentUid, row.QuestionId, row.Question, row.AnswerHash, false, false, row.SourceFingerprint));
        Assert.False(PortalImport.ExistingMatches(row, row.LegacyStudentUid, row.QuestionId, row.Question, row.AnswerHash, false, true, row.SourceFingerprint));
        Assert.False(PortalImport.ExistingMatches(row, row.LegacyStudentUid, row.QuestionId, row.Question, "changed hash", false, false, row.SourceFingerprint));
        Assert.False(PortalImport.ExistingMatches(row, 1000, row.QuestionId, row.Question, row.AnswerHash, false, false, row.SourceFingerprint));
    }
    [Theory]
    [InlineData("Server=other;Database=CAMS_Enterprise;Encrypt=True")]
    [InlineData("Server=windb3;Database=other;Encrypt=True")]
    [InlineData("Server=windb3;Database=CAMS_Enterprise;Encrypt=False")]
    [InlineData("Server=windb3;Database=CAMS_Enterprise;TrustServerCertificate=True")]
    public void SQL_sources_must_match_pins_and_trusted_TLS(string value)
    { Assert.Throws<ImportFailure>(() => ImportModel.SqlConnection(value, "windb3", "CAMS_Enterprise")); }
    [Fact] public void SQL_source_connections_are_read_only_and_bounded()
    {
        var b = new SqlConnectionStringBuilder(ImportModel.SqlConnection("Server=tcp:windb3,1433;Database=CAMS_Enterprise;Encrypt=True;Connect Timeout=0", "windb3", "CAMS_Enterprise"));
        Assert.Equal(ApplicationIntent.ReadOnly, b.ApplicationIntent); Assert.Equal(10, b.ConnectTimeout);
        Assert.False(b.PersistSecurityInfo); Assert.Equal(0, b.ConnectRetryCount);
    }
    [Fact] public void Portal_target_is_pinned_to_selected_environment_and_verifies_server_certificate()
    {
        const string np = "Host=psql-campusflow-prod-001.postgres.database.azure.com;Database=campusflow_nonprod;SSL Mode=Require";
        var b = new NpgsqlConnectionStringBuilder(ImportModel.PortalConnection(np, "nonprod")); Assert.Equal(SslMode.VerifyFull, b.SslMode);
        Assert.Throws<ImportFailure>(() => ImportModel.PortalConnection(np, "production"));
    }
    [Fact] public void Private_export_is_create_new_mode_0600_and_matches_the_reviewed_SHA256()
    {
        using var folder = new PrivateFolder(); var file = Path.Combine(folder.Path, "proof.json"); var bytes = Encoding.UTF8.GetBytes("{\"formatVersion\":1}");
        var hash = PrivateManifestFile.Write(file, bytes); Assert.Equal(bytes, PrivateManifestFile.Read(file, hash));
        Assert.Throws<IOException>(() => PrivateManifestFile.Write(file, bytes));
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.Read(file, new string('0', 64)));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Export_cannot_write_inside_a_Git_checkout_or_worktree(bool worktree)
    {
        using var folder = new PrivateFolder(); var repo = Path.Combine(folder.Path, "repo"); Directory.CreateDirectory(repo);
        if (worktree) File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: synthetic"); else Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.OutputPath(Path.Combine(repo, "nested", "proof.json")));
    }
    [Fact] public void Export_cannot_write_inside_a_bare_Git_repository()
    {
        using var folder = new PrivateFolder(); File.WriteAllText(Path.Combine(folder.Path, "HEAD"), "ref: refs/heads/synthetic");
        Directory.CreateDirectory(Path.Combine(folder.Path, "objects")); Directory.CreateDirectory(Path.Combine(folder.Path, "refs"));
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.OutputPath(Path.Combine(folder.Path, "proof.json")));
    }
    [Fact] public void Relative_paths_and_ambiguous_duplicate_JSON_keys_are_rejected()
    {
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.OutputPath("proof.json"));
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.RejectDuplicateProperties(Encoding.UTF8.GetBytes("{\"environment\":\"nonprod\",\"environment\":\"production\"}")));
    }
    [Fact] public void Public_readable_manifests_are_not_accepted()
    {
        if (OperatingSystem.IsWindows()) return;
        using var folder = new PrivateFolder(); var file = Path.Combine(folder.Path, "proof.json"); var bytes = Encoding.UTF8.GetBytes("{}");
        var hash = PrivateManifestFile.Write(file, bytes); File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead);
        Assert.Throws<ImportFailure>(() => PrivateManifestFile.Read(file, hash));
    }
    [Fact] public void Reuse_options_are_paired_export_only_and_require_a_hash()
    {
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", "/private/tmp/old.json"]));
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ExportArgs(), "--reuse-sha256", new string('a', 64)]));
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", "/private/tmp/old.json", "--reuse-sha256", "invalid"]));
        Assert.Throws<ImportFailure>(() => ImportOptions.Parse([.. ApplyArgs(), "--reuse-manifest", "/private/tmp/old.json", "--reuse-sha256", new string('a', 64)]));
        var valid = ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", "/private/tmp/old.json", "--reuse-sha256", new string('A', 64)]);
        Assert.Equal("/private/tmp/old.json", valid.ReuseManifest); Assert.Equal(new string('a', 64), valid.ReuseSha256);
    }
    [Fact] public void Pilot_to_bulk_reuses_the_prior_salt_after_verifying_the_current_answer()
    {
        var pilot = Manifest(); pilot.Records.Add(Row(pilot));
        var bulk = Manifest(); bulk.Selection = "all-explicit";
        var reuse = ReusableManifest.FromValidated(pilot, bulk);
        var same = ImportModel.Record(bulk, new(42, "00001234"), new(999, "00001234"),
            [new(7, "A synthetic question?", "  SYNTHETIC PRIVATE ANSWER  ")], false, reuse.Find(42));
        bulk.Records.Add(same); bulk.Records.Add(Row(bulk, 43, 1000, "00005678"));
        reuse.EnsurePreserved(bulk);
        Assert.Equal(pilot.Records[0].AnswerHash, same.AnswerHash);
        Assert.Equal(pilot.Records[0].SourceFingerprint, same.SourceFingerprint);
        Assert.Equal(2, bulk.Records.Count); Assert.Null(reuse.Find(43));
    }
    [Fact] public void A_changed_source_answer_stops_export_instead_of_replacing_the_imported_proof()
    {
        var pilot = Manifest(); var prior = Row(pilot); pilot.Records.Add(prior);
        var failure = Assert.Throws<ImportFailure>(() => ImportModel.Record(Manifest(), new(42, "00001234"), new(999, "00001234"),
            [new(7, "A synthetic question?", "Different synthetic answer")], false, prior));
        Assert.Equal("PriorVerificationChangedRequiresReview", failure.Code);
        Assert.DoesNotContain("Different synthetic answer", failure.Message);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Unchanged_blocked_or_no_question_proofs_preserve_the_original_fingerprint(bool allowed)
    {
        var previous = Manifest(); previous.Records.Add(ImportModel.Record(previous, new(42, "00001234"), new(999, "00001234"), [], allowed));
        var output = Manifest(); var reuse = ReusableManifest.FromValidated(previous, output);
        var row = ImportModel.Record(output, new(42, "00001234"), new(999, "00001234"), [], allowed, reuse.Find(42));
        output.Records.Add(row); reuse.EnsurePreserved(output);
        Assert.Null(row.AnswerHash); Assert.Equal(previous.Records[0].SourceFingerprint, row.SourceFingerprint);
    }
    [Fact] public void Changed_mapping_question_flags_and_missing_prior_rows_stop_for_review()
    {
        var previous = Manifest(); var prior = Row(previous); previous.Records.Add(prior); var output = Manifest();
        var reuse = ReusableManifest.FromValidated(previous, output);
        Assert.Throws<ImportFailure>(() => ImportModel.Record(output, new(42, "00001234"), new(1000, "00001234"), [new(7, "A synthetic question?", "Synthetic private answer")], false, prior));
        Assert.Throws<ImportFailure>(() => ImportModel.Record(output, new(42, "00001234"), new(999, "00001234"), [new(7, "Changed question?", "Synthetic private answer")], false, prior));
        Assert.Throws<ImportFailure>(() => ImportModel.Record(output, new(42, "00001234"), new(999, "00001234"), [], true, prior));
        Assert.Throws<ImportFailure>(() => reuse.EnsurePreserved(output));
    }
    [Fact] public void Reuse_load_checks_private_artifact_hash_environment_and_fingerprints()
    {
        using var folder = new PrivateFolder(); var file = Path.Combine(folder.Path, "pilot.json");
        var previous = Manifest(); previous.Records.Add(Row(previous));
        var hash = PrivateManifestFile.Write(file, JsonSerializer.SerializeToUtf8Bytes(previous, ImportModel.Json));
        var options = ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", file, "--reuse-sha256", hash]);
        Assert.NotNull(ReusableManifest.Load(options, Manifest()));
        var wrongHash = ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", file, "--reuse-sha256", new string('0', 64)]);
        Assert.Throws<ImportFailure>(() => ReusableManifest.Load(wrongHash, Manifest()));
        var wrongEnvironment = ExportArgs(); wrongEnvironment[2] = "production";
        Assert.Throws<ImportFailure>(() => ReusableManifest.Load(ImportOptions.Parse([.. wrongEnvironment, "--reuse-manifest", file, "--reuse-sha256", hash]), Manifest()));
        previous.Records[0].Question = "Mutated question?";
        var changedFile = Path.Combine(folder.Path, "changed.json");
        var changedHash = PrivateManifestFile.Write(changedFile, JsonSerializer.SerializeToUtf8Bytes(previous, ImportModel.Json));
        Assert.Throws<ImportFailure>(() => ReusableManifest.Load(ImportOptions.Parse([.. ExportArgs(), "--reuse-manifest", changedFile, "--reuse-sha256", changedHash]), Manifest()));
    }
    [Fact] public void Reuse_rejects_changed_source_collation_or_canonical_metadata()
    {
        var previous = Manifest(); previous.Records.Add(Row(previous));
        var context = Manifest(); context.LegacyAnswerCollation = "Latin1_General_CI_AS";
        Assert.Throws<ImportFailure>(() => ReusableManifest.FromValidated(previous, context));
        context = Manifest(); context.LegacyServer = "WINDB3";
        Assert.Throws<ImportFailure>(() => ReusableManifest.FromValidated(previous, context));
    }
    private sealed class PrivateFolder : IDisposable
    {
        public string Path { get; }
        public PrivateFolder()
        {
            Path = System.IO.Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : System.IO.Path.GetTempPath(), "campusflow-recovery-test-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path); else Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public void Dispose() { Directory.Delete(Path, true); }
    }
}
