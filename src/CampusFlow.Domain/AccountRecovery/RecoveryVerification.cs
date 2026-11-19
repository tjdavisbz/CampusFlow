using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.AccountRecovery;

public sealed class RecoveryVerification : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int StudentUid { get; private set; }
    public int LegacyStudentUid { get; private set; }
    public int? QuestionId { get; private set; }
    public string? Question { get; private set; }
    public string? AnswerHash { get; private set; }
    public bool LegacyNoQuestionAllowed { get; private set; }
    public bool IsBlocked { get; private set; }
    public string SourceFingerprint { get; private set; } = "";
    private RecoveryVerification() { }
    public RecoveryVerification(Guid id, Guid tenant, int studentUid, int legacyStudentUid, int? questionId,
        string? question, string? answerHash, bool noQuestionAllowed, bool blocked, string fingerprint) : base(id)
    {
        if (tenant == Guid.Empty || studentUid <= 0 || legacyStudentUid <= 0 || fingerprint.Length != 64)
            throw new ArgumentException("An imported verification requires a valid student mapping and source fingerprint.");
        if (!blocked && (questionId.HasValue ? questionId <= 0 || string.IsNullOrWhiteSpace(question) ||
            question.Length > 500 || !RecoveryAnswerHash.IsValid(answerHash) || noQuestionAllowed : !noQuestionAllowed || answerHash is not null || question is not null))
            throw new ArgumentException("The imported verification is incomplete or ambiguous.");
        TenantId = tenant; StudentUid = studentUid; LegacyStudentUid = legacyStudentUid;
        QuestionId = questionId; Question = question; AnswerHash = answerHash;
        LegacyNoQuestionAllowed = noQuestionAllowed; IsBlocked = blocked; SourceFingerprint = fingerprint;
    }
    public override string ToString() => "Imported account verification [redacted]";
}

public interface IAccountRecoveryVerificationStore
{
    Task<RecoveryVerification?> GetAsync(int studentUid, CancellationToken ct = default);
}

public static class RecoveryAnswerHash
{
    public const int Iterations = 600000;
    private const string Prefix = "PBKDF2-SHA256";
    public static string Create(string answer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        if (answer.Length > 500) throw new ArgumentException("Verification answer is too long.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var bytes = Encoding.UTF8.GetBytes(Normalize(answer));
        try { return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Iterations, HashAlgorithmName.SHA256, 32))}"; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static bool IsValid(string? encoded) => TryParse(encoded, out _, out _);
    public static bool Verify(string answer, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 500 || !TryParse(encoded, out var salt, out var expected)) return false;
        var bytes = Encoding.UTF8.GetBytes(Normalize(answer));
        try { return CryptographicOperations.FixedTimeEquals(expected, Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Iterations, HashAlgorithmName.SHA256, 32)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static string Normalize(string answer) => answer.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    private static bool TryParse(string? encoded, out byte[] salt, out byte[] hash)
    {
        salt = []; hash = [];
        if (encoded is null || encoded.Length > 160) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || parts[1] != Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        try { salt = Convert.FromBase64String(parts[2]); hash = Convert.FromBase64String(parts[3]); return salt.Length == 16 && hash.Length == 32; }
        catch (FormatException) { return false; }
    }
}
