using System.Data;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CampusFlow.LegacyRecoveryImport;

internal static class SqlSources
{
    internal static string RequiredEnvironment(string name) =>
        System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new ImportFailure("EnvironmentConnectionMissing");

    internal static async Task<(List<StudentIdentifier> Students, HashSet<int> Eligible)> StudentsAsync(SqlConnection connection, string? selected, bool current)
    {
        // Read IDs separately so students without an account remain in duplicate-ID checks.
        const string sql = "SELECT s.StudentUID,LTRIM(RTRIM(s.StudentID)) FROM dbo.Student s WHERE s.StudentID IS NOT NULL AND LTRIM(RTRIM(s.StudentID))<>'' AND (@StudentId IS NULL OR LTRIM(RTRIM(s.StudentID))=@StudentId) ORDER BY s.StudentUID;";
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.Add("@StudentId", SqlDbType.NVarChar, 50).Value = (object?)selected ?? DBNull.Value;
        var students = new List<StudentIdentifier>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var uid = reader.GetInt32(0); var id = ImportModel.StudentId(reader.GetString(1));
                if (uid <= 0) throw new ImportFailure("InvalidStudentMapping");
                students.Add(new(uid, id));
            }
        }
        if (!current) return (students, []);
        // One bounded scan avoids a correlated ownership lookup for every student.
        using var addresses = new SqlCommand(CampusFlow.AccountRecovery.RecoveryStudentEmailSql.ActiveAddresses, connection) { CommandTimeout = 120 };
        var rows = new List<(int Uid, string? Email)>();
        await using (var reader = await addresses.ExecuteReaderAsync())
            while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return (students, EligibleEmailOwners(rows));
    }

    internal static HashSet<int> EligibleEmailOwners(IReadOnlyList<(int Uid, string? Email)> addresses)
    {
        var normalized = addresses.Select(x => (x.Uid, Upn: x.Email is null ? null : SchoolUpn(x.Email))).ToArray();
        var owners = normalized.Where(x => x.Upn is not null).GroupBy(x => x.Upn!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Uid).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        return normalized.GroupBy(x => x.Uid).Where(g => g.Key > 0 && g.Count() == 1 && g.Single().Upn is string upn && owners[upn] == 1)
            .Select(g => g.Key).ToHashSet();
    }

    internal static string? SchoolUpn(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > 254 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return null;
        try
        {
            var mail = new MailAddress(value);
            return mail.Host.Equals("my.nelson.edu", StringComparison.OrdinalIgnoreCase) && mail.Address.Equals(value, StringComparison.OrdinalIgnoreCase) && mail.DisplayName.Length == 0 ? value.ToLowerInvariant() : null;
        }
        catch (FormatException) { return null; }
    }

    internal static SqlCommand QuestionsCommand(SqlConnection? connection, int legacyUid)
    {
        if (legacyUid <= 0) throw new ImportFailure("InvalidStudentMapping");
        var command = new SqlCommand("""
            SELECT TOP (2) stored.QuestionID, questions.Question, stored.Answer
            FROM dbo.SAGU_Student_SecretQuestion stored
            LEFT JOIN dbo.SAGU_SecretQuestions questions ON questions.QuestionID=stored.QuestionID
            WHERE stored.StudentUID=@LegacyStudentUid ORDER BY stored.QuestionID;
            """, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@LegacyStudentUid", SqlDbType.Int).Value = legacyUid;
        return command;
    }

    internal static async Task<List<LegacyQuestion>> QuestionsAsync(SqlConnection connection, int legacyUid)
    {
        using var command = QuestionsCommand(connection, legacyUid); var rows = new List<LegacyQuestion>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new(reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    internal static SqlCommand NoQuestionCommand(SqlConnection? connection, int legacyUid)
    {
        if (legacyUid <= 0) throw new ImportFailure("InvalidStudentMapping");
        // Export only the caller's exact eligibility predicate, never the raw FERPA value.
        var command = new SqlCommand("SELECT CASE WHEN dbo.FERPA(@LegacyStudentUid) <> -9999999 THEN 1 ELSE 0 END;", connection) { CommandTimeout = 30 };
        command.Parameters.Add("@LegacyStudentUid", SqlDbType.Int).Value = legacyUid; return command;
    }

    internal static async Task<object> ExportAsync(ImportOptions options)
    {
        _ = PrivateManifestFile.OutputPath(options.File); // Refuse repository/symlink paths before reading verification data.
        var target = ImportModel.ElementsTarget(options.Environment);
        var legacyValue = ImportModel.SqlConnection(RequiredEnvironment("CAMPUSFLOW_RECOVERY_LEGACY_CONNECTION_STRING"), options.LegacyServer, options.LegacyDatabase);
        var currentValue = ImportModel.SqlConnection(RequiredEnvironment("CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING"), target.Server, target.Database);
        await using var legacy = new SqlConnection(legacyValue); await legacy.OpenAsync();
        await using var current = new SqlConnection(currentValue); await current.OpenAsync();
        using (var schema = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.SAGU_Student_SecretQuestion',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SAGU_SecretQuestions',N'U') IS NOT NULL THEN 1 ELSE 0 END;", legacy))
            if (Convert.ToInt32(await schema.ExecuteScalarAsync()) != 1) throw new ImportFailure("LegacyVerificationTablesUnavailable");
        bool ferpaAvailable;
        using (var schema = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.FERPA') IS NOT NULL AND OBJECTPROPERTY(OBJECT_ID(N'dbo.FERPA'),N'IsScalarFunction')=1 THEN 1 ELSE 0 END;", legacy))
            ferpaAvailable = Convert.ToInt32(await schema.ExecuteScalarAsync()) == 1;
        string databaseCollation, answerCollation;
        using (var query = new SqlCommand("SELECT CONVERT(nvarchar(128),DATABASEPROPERTYEX(DB_NAME(),N'Collation'));SELECT collation_name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SAGU_Student_SecretQuestion') AND name=N'Answer';", legacy))
        {
            await using var reader = await query.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.IsDBNull(0)) throw new ImportFailure("LegacyAnswerCollationRequiresReview");
            databaseCollation = reader.GetString(0);
            await reader.NextResultAsync();
            if (!await reader.ReadAsync() || reader.IsDBNull(0)) throw new ImportFailure("LegacyAnswerCollationRequiresReview");
            answerCollation = reader.GetString(0);
        }
        if (!ImportModel.SupportedAnswerCollation(databaseCollation) || !ImportModel.SupportedAnswerCollation(answerCollation))
            throw new ImportFailure("LegacyAnswerCollationRequiresReview");
        var legacyStudents = (await StudentsAsync(legacy, options.StudentId, false)).Students;
        // Keep all current StudentID occurrences for ambiguity checks, even when only one has an institutional account.
        var currentStudents = await StudentsAsync(current, null, true);
        var currentIndex = currentStudents.Students.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var manifest = new RecoveryManifest { Environment = options.Environment, CreatedAtUtc = DateTime.UtcNow,
            LegacyServer = options.LegacyServer, LegacyDatabase = options.LegacyDatabase,
            LegacyDatabaseCollation = databaseCollation, LegacyAnswerCollation = answerCollation,
            ElementsServer = target.Server, ElementsDatabase = target.Database, Selection = options.All ? "all-explicit" : "single-student" };
        var reuse = ReusableManifest.Load(options, manifest);
        foreach (var group in legacyStudents.GroupBy(x => x.Id, StringComparer.Ordinal))
        {
            if (group.Count() != 1 || !currentIndex.TryGetValue(group.Key, out var matches) || matches.Length != 1 ||
                !currentStudents.Eligible.Contains(matches[0].Uid)) { manifest.MissingOrAmbiguousMappings++; continue; }
            var source = group.Single(); var questions = await QuestionsAsync(legacy, source.Uid); bool? mayContinue = null;
            if (questions.Count == 0 && ferpaAvailable)
            { using var predicate = NoQuestionCommand(legacy, source.Uid); mayContinue = Convert.ToInt32(await predicate.ExecuteScalarAsync()) == 1; }
            manifest.Records.Add(ImportModel.Record(manifest, matches[0], source, questions, mayContinue, reuse?.Find(matches[0].Uid)));
        }
        if (manifest.Records.Count is < 1 or > 100000) throw new ImportFailure("NoImportableOrOversizedMapping");
        reuse?.EnsurePreserved(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportModel.Json);
        try
        {
            var hash = PrivateManifestFile.Write(options.File, bytes);
            return new { action = "export", environment = options.Environment, records = manifest.Records.Count,
                blocked = manifest.Records.Count(x => x.IsBlocked), skippedMappings = manifest.MissingOrAmbiguousMappings,
                sha256 = hash, privateFileCreated = true, plaintextAnswersExported = false,
                ssnOrBirthdatesExported = false, portalPasswordsExported = false, accountOrPasswordChanges = false };
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
