using System.Security.Cryptography;
using System.Text.Json;

namespace CampusFlow.LegacyRecoveryImport;

internal sealed class ReusableManifest
{
    private readonly Dictionary<int, RecoveryImportRecord> _records;
    private ReusableManifest(RecoveryManifest manifest) => _records = manifest.Records.ToDictionary(x => x.StudentUid);

    internal static ReusableManifest? Load(ImportOptions options, RecoveryManifest context)
    {
        if (options.ReuseManifest is null) return null;
        var bytes = PrivateManifestFile.Read(options.ReuseManifest, options.ReuseSha256!);
        RecoveryManifest manifest;
        try { manifest = JsonSerializer.Deserialize<RecoveryManifest>(bytes, ImportModel.Json) ?? throw new ImportFailure("InvalidReuseManifest"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        ImportModel.ValidateManifest(manifest, options);
        return FromValidated(manifest, context);
    }

    internal static ReusableManifest FromValidated(RecoveryManifest previous, RecoveryManifest context)
    {
        // Exact context equality preserves the canonical fingerprint used by the original import.
        if (previous.FormatVersion != context.FormatVersion || previous.Environment != context.Environment ||
            previous.TenantName != context.TenantName || previous.LegacyServer != context.LegacyServer ||
            previous.LegacyDatabase != context.LegacyDatabase || previous.LegacyDatabaseCollation != context.LegacyDatabaseCollation ||
            previous.LegacyAnswerCollation != context.LegacyAnswerCollation || previous.AnswerNormalization != context.AnswerNormalization ||
            previous.ElementsServer != context.ElementsServer || previous.ElementsDatabase != context.ElementsDatabase)
            throw new ImportFailure("ReuseManifestContextChanged");
        return new(previous);
    }

    internal RecoveryImportRecord? Find(int currentStudentUid) => _records.GetValueOrDefault(currentStudentUid);

    internal void EnsurePreserved(RecoveryManifest output)
    {
        var exported = output.Records.ToDictionary(x => x.StudentUid);
        foreach (var row in _records.Values)
            if (!exported.TryGetValue(row.StudentUid, out var current) || current.SourceFingerprint != row.SourceFingerprint)
                throw new ImportFailure("PriorVerificationOmittedOrChangedRequiresReview");
    }
}
