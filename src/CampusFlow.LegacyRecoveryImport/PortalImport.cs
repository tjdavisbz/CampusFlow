using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace CampusFlow.LegacyRecoveryImport;

internal static class PortalImport
{
    private sealed record ExistingProof(int LegacyUid, int? QuestionId, string? Question, string? Hash,
        bool Allowed, bool Blocked, string Fingerprint);
    internal static bool ExistingMatches(RecoveryImportRecord record, int legacyUid, int? questionId,
        string? question, string? answerHash, bool allowed, bool blocked, string fingerprint) =>
        record.LegacyStudentUid == legacyUid && record.QuestionId == questionId && record.Question == question &&
        record.AnswerHash == answerHash && record.LegacyNoQuestionAllowed == allowed && record.IsBlocked == blocked &&
        record.SourceFingerprint == fingerprint;

    private static async Task<Dictionary<int, ExistingProof>> ReadProofsAsync(NpgsqlConnection database,
        NpgsqlTransaction transaction, Guid tenant, RecoveryManifest manifest)
    {
        var result = new Dictionary<int, ExistingProof>();
        using (var query = new NpgsqlCommand("SELECT \"StudentUid\",\"LegacyStudentUid\",\"QuestionId\",\"Question\",\"AnswerHash\",\"LegacyNoQuestionAllowed\",\"IsBlocked\",\"SourceFingerprint\" FROM \"AppAccountRecoveryVerifications\" WHERE \"TenantId\"=@Tenant AND \"StudentUid\"=ANY(@Students)", database, transaction))
        {
            query.Parameters.AddWithValue("Tenant", tenant); query.Parameters.AddWithValue("Students", NpgsqlDbType.Array | NpgsqlDbType.Integer, manifest.Records.Select(x => x.StudentUid).ToArray());
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (!result.TryAdd(reader.GetInt32(0), new(reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetString(7))))
                    throw new ImportFailure("DuplicateExistingProof");
        }
        return result;
    }

    internal static async Task<object> RunAsync(ImportOptions options)
    {
        var bytes = PrivateManifestFile.Read(options.File, options.ExpectedSha256!);
        RecoveryManifest manifest;
        try { manifest = JsonSerializer.Deserialize<RecoveryManifest>(bytes, ImportModel.Json) ?? throw new ImportFailure("InvalidManifest"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        ImportModel.ValidateManifest(manifest, options);
        var target = ImportModel.ElementsTarget(options.Environment);
        var elementsValue = ImportModel.SqlConnection(SqlSources.RequiredEnvironment("CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING"), target.Server, target.Database);
        await using (var elements = new Microsoft.Data.SqlClient.SqlConnection(elementsValue))
        {
            await elements.OpenAsync(); var current = await SqlSources.StudentsAsync(elements, null, true);
            var byUid = current.Students.GroupBy(x => x.Uid).ToDictionary(g => g.Key, g => g.ToArray());
            var byId = current.Students.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var row in manifest.Records)
                if (!byUid.TryGetValue(row.StudentUid, out var students) || students.Length != 1 || byId[students[0].Id] != 1 ||
                    !current.Eligible.Contains(row.StudentUid) || ImportModel.StudentIdHash(students[0].Id) != row.StudentIdHash)
                    throw new ImportFailure("CurrentStudentMappingChanged");
        }
        var connection = ImportModel.PortalConnection(SqlSources.RequiredEnvironment("CAMPUSFLOW_RECOVERY_PORTAL_CONNECTION_STRING"), options.Environment);
        await using var database = new NpgsqlConnection(connection); await database.OpenAsync();
        await using var transaction = await database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (!options.Apply)
        { using var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", database, transaction); await ro.ExecuteNonQueryAsync(); }
        else
        { using var limit = new NpgsqlCommand("SET LOCAL lock_timeout='5s';SET LOCAL statement_timeout='30s';SELECT pg_advisory_xact_lock(hashtextextended('CampusFlow.RecoveryVerificationImport',0))", database, transaction); await limit.ExecuteNonQueryAsync(); }
        Guid tenant;
        using (var query = new NpgsqlCommand("SELECT \"Id\" FROM \"AbpTenants\" WHERE lower(\"Name\")='nelson' AND NOT \"IsDeleted\"", database, transaction))
        { await using var reader = await query.ExecuteReaderAsync(); if (!await reader.ReadAsync()) throw new ImportFailure("NelsonTenantMissing"); tenant = reader.GetGuid(0); if (await reader.ReadAsync() || tenant == Guid.Empty) throw new ImportFailure("AmbiguousTenant"); }
        using (var query = new NpgsqlCommand("SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND tablename='AppAccountRecoveryVerifications' AND indexname='IX_AppAccountRecoveryVerifications_TenantId_StudentUid' AND indexdef LIKE 'CREATE UNIQUE INDEX%'", database, transaction))
            if (Convert.ToInt32(await query.ExecuteScalarAsync()) != 1) throw new ImportFailure("ReviewedVerificationSchemaRequired");
        var existing = await ReadProofsAsync(database, transaction, tenant, manifest);
        var missing = new List<RecoveryImportRecord>();
        foreach (var row in manifest.Records)
        {
            if (!existing.TryGetValue(row.StudentUid, out var proof)) missing.Add(row);
            else if (!ExistingMatches(row, proof.LegacyUid, proof.QuestionId, proof.Question, proof.Hash, proof.Allowed, proof.Blocked, proof.Fingerprint))
                throw new ImportFailure("ExistingProofRequiresExplicitReview");
        }
        if (options.Apply)
        {
            foreach (var row in missing)
            {
                using var insert = new NpgsqlCommand("""
                    INSERT INTO "AppAccountRecoveryVerifications"
                    ("Id","TenantId","StudentUid","LegacyStudentUid","QuestionId","Question","AnswerHash",
                     "LegacyNoQuestionAllowed","IsBlocked","SourceFingerprint","ExtraProperties","ConcurrencyStamp",
                     "CreationTime","CreatorId","LastModificationTime","LastModifierId")
                    VALUES (@Id,@Tenant,@Current,@Legacy,@QuestionId,@Question,@Hash,@Allowed,@Blocked,@Fingerprint,'{}',@Concurrency,@Created,NULL,NULL,NULL)
                    """, database, transaction);
                insert.Parameters.AddWithValue("Id", Guid.NewGuid()); insert.Parameters.AddWithValue("Tenant", tenant);
                insert.Parameters.AddWithValue("Current", row.StudentUid); insert.Parameters.AddWithValue("Legacy", row.LegacyStudentUid);
                insert.Parameters.AddWithValue("QuestionId", NpgsqlDbType.Integer, (object?)row.QuestionId ?? DBNull.Value);
                insert.Parameters.AddWithValue("Question", NpgsqlDbType.Varchar, (object?)row.Question ?? DBNull.Value);
                insert.Parameters.AddWithValue("Hash", NpgsqlDbType.Varchar, (object?)row.AnswerHash ?? DBNull.Value);
                insert.Parameters.AddWithValue("Allowed", row.LegacyNoQuestionAllowed); insert.Parameters.AddWithValue("Blocked", row.IsBlocked);
                insert.Parameters.AddWithValue("Fingerprint", row.SourceFingerprint); insert.Parameters.AddWithValue("Concurrency", Guid.NewGuid().ToString("N"));
                insert.Parameters.AddWithValue("Created", NpgsqlDbType.Timestamp, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified));
                if (await insert.ExecuteNonQueryAsync() != 1) throw new ImportFailure("VerificationInsertUnconfirmed");
            }
            var after = await ReadProofsAsync(database, transaction, tenant, manifest);
            if (after.Count != manifest.Records.Count || manifest.Records.Any(row => !after.TryGetValue(row.StudentUid, out var proof) ||
                !ExistingMatches(row, proof.LegacyUid, proof.QuestionId, proof.Question, proof.Hash, proof.Allowed, proof.Blocked, proof.Fingerprint)))
                throw new ImportFailure("VerificationReadbackFailed");
            await transaction.CommitAsync();
        }
        else await transaction.RollbackAsync();
        return new { action = options.Apply ? "apply" : "dry-run", environment = options.Environment,
            records = manifest.Records.Count, wouldInsert = missing.Count, inserted = options.Apply ? missing.Count : 0,
            unchanged = existing.Count, blocked = manifest.Records.Count(x => x.IsBlocked), insertsOnly = true,
            accountOrPasswordChanges = false, permissionsChanged = false, runtimeEnabled = false };
    }
}
