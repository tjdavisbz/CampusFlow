using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Auditing;

namespace CampusFlow.AccountRecovery;

[DisableAuditing]
public sealed class AccountRecoveryIdentity
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Ssn { get; init; } = "";
    public DateOnly BirthDate { get; init; }
    public override string ToString() => "Account recovery identity [redacted]";
}

public sealed record RecoveryPerson(int StudentUid, int LegacyStudentUid, string UserPrincipalName);
public sealed record RecoveryQuestion(bool SourceAvailable, int? Id, string? Text, bool MayContinueWithoutQuestion, string? SourceFingerprint = null);
public sealed record RecoveryDirectoryAccount(Guid ObjectId, string UserPrincipalName, bool Enabled, bool IsMember,
    bool IsPrivileged, bool IsSynchronized);
public enum RecoveryPasswordWriteResult { Accepted, PolicyRejected, Unavailable, Unknown }
public enum RecoveryNotice { None, NotVerified, RateLimited, PasswordMismatch, PasswordPolicy, Expired }
public enum RecoveryStep { Identity, Question, Password, Complete, Unavailable, Unknown }

[DisableAuditing]
public sealed class AccountRecoveryResult
{
    public RecoveryStep Step { get; init; }
    public string? Ticket { get; init; }
    public string? Question { get; init; }
    public string? Message { get; init; }
    public RecoveryNotice Notice { get; init; }
    public override string ToString() => $"Account recovery result: {Step} [ticket redacted]";
}

public interface IAccountRecoveryWorkflow
{
    bool IsAvailable { get; }
    Task<AccountRecoveryResult> ResumeAsync(string? ticket, string clientAddress, CancellationToken ct = default);
    Task<AccountRecoveryResult> StartAsync(AccountRecoveryIdentity identity, string clientAddress, CancellationToken ct = default);
    Task<AccountRecoveryResult> AnswerAsync(string? ticket, string answer, string clientAddress, CancellationToken ct = default);
    Task<AccountRecoveryResult> ResetAsync(string? ticket, string password, string confirmation, string clientAddress, CancellationToken ct = default);
}

public interface IAccountRecoveryIdentitySource
{
    bool IsConfigured { get; }
    Task<RecoveryPerson?> VerifyIdentityAsync(AccountRecoveryIdentity identity, CancellationToken ct = default);
    Task<RecoveryQuestion> GetQuestionAsync(RecoveryPerson person, CancellationToken ct = default);
    Task<bool> RevalidatePersonAsync(RecoveryPerson person, CancellationToken ct = default);
    Task<bool> VerifyAnswerAsync(RecoveryPerson person, int questionId, string answer, CancellationToken ct = default);
}

public interface IAccountRecoveryDirectory
{
    bool SupportsSynchronizedAccounts { get; }
    bool IsConfigured { get; }
    Task<RecoveryDirectoryAccount?> FindAsync(string userPrincipalName, CancellationToken ct = default);
    Task<RecoveryPasswordWriteResult> ResetAsync(RecoveryDirectoryAccount account, string password, CancellationToken ct = default);
}
