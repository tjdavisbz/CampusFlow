using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.AccountRecovery;

public sealed class RecoveryRequest : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string TicketHash { get; private set; } = "";
    public string ClientHash { get; private set; } = "";
    public DateTime ExpiresAtUtc { get; private set; }
    public int? StudentUid { get; private set; }
    public int? LegacyStudentUid { get; private set; }
    public string? UserPrincipalName { get; private set; }
    public Guid? DirectoryObjectId { get; private set; }
    public int? QuestionId { get; private set; }
    public string? VerificationFingerprint { get; private set; }
    public DateTime? PasswordWriteFinishedAtUtc { get; private set; }
    public int AnswerAttempts { get; private set; }
    public int PasswordAttempts { get; private set; }
    public string State { get; private set; } = "Started";
    private RecoveryRequest() { }
    public RecoveryRequest(Guid id, Guid tenant, string ticketHash, string clientHash, DateTime now) : base(id)
    {
        if (tenant == Guid.Empty || ticketHash.Length != 64 || clientHash.Length != 64) throw new ArgumentException("Recovery request scope is invalid.");
        TenantId = tenant; TicketHash = ticketHash; ClientHash = clientHash; CreationTime = now; ExpiresAtUtc = now.AddMinutes(10);
    }
    public bool Active(DateTime now) => ExpiresAtUtc > now;
    public bool Bind(RecoveryPerson person, RecoveryDirectoryAccount account, RecoveryQuestion question, DateTime now)
    {
        if (State != "Started" || !Active(now) || !question.SourceAvailable ||
            question.SourceFingerprint is not { Length: 64 } || !question.SourceFingerprint.All(char.IsAsciiHexDigit) ||
            (!question.Id.HasValue && !question.MayContinueWithoutQuestion)) return false;
        if (person.StudentUid <= 0 || person.LegacyStudentUid <= 0 || account.ObjectId == Guid.Empty ||
            !string.Equals(person.UserPrincipalName, account.UserPrincipalName, StringComparison.OrdinalIgnoreCase)) return false;
        StudentUid = person.StudentUid; LegacyStudentUid = person.LegacyStudentUid; UserPrincipalName = account.UserPrincipalName;
        DirectoryObjectId = account.ObjectId; QuestionId = question.Id; VerificationFingerprint = question.SourceFingerprint;
        State = question.Id.HasValue ? "Question" : "Verified"; return true;
    }
    public bool BeginAnswer(DateTime now)
    {
        if (State != "Question" || !Active(now) || AnswerAttempts >= 3) return false;
        AnswerAttempts++; State = "Checking"; return true;
    }
    public void FinishAnswer(bool verified)
    { if (State == "Checking") State = verified ? "Verified" : AnswerAttempts >= 3 ? "Blocked" : "Question"; }
    public bool ClaimReset(DateTime now)
    {
        if (State != "Verified" || !Active(now) || PasswordAttempts >= 3) return false;
        PasswordAttempts++; State = "Applying"; return true;
    }
    public void Finish(RecoveryPasswordWriteResult outcome, DateTime? now = null)
    {
        if (State != "Applying") return;
        if (outcome is RecoveryPasswordWriteResult.Accepted or RecoveryPasswordWriteResult.Unknown)
            PasswordWriteFinishedAtUtc = now ?? DateTime.UtcNow;
        State = outcome switch { RecoveryPasswordWriteResult.Accepted => "Complete",
            RecoveryPasswordWriteResult.PolicyRejected when PasswordAttempts < 3 => "Verified",
            RecoveryPasswordWriteResult.Unknown => "Unknown", _ => "Blocked" };
    }
    public void Reject() { if (State is not ("Applying" or "Complete" or "Unknown")) State = "Blocked"; }
    public RecoveryPerson? Person => StudentUid is > 0 && LegacyStudentUid is > 0 && UserPrincipalName is not null
        ? new(StudentUid.Value, LegacyStudentUid.Value, UserPrincipalName) : null;
    public override string ToString() => $"Account recovery request: {State} [details redacted]";
}

public enum RecoveryBindResult { Bound, RateLimited, PendingReset, Expired }

public interface IAccountRecoveryRequestStore
{
    Task<bool> AddAsync(RecoveryRequest request, DateTime now, CancellationToken ct = default);
    Task<RecoveryRequest?> FindAsync(string ticketHash, CancellationToken ct = default);
    Task<RecoveryBindResult> BindAsync(Guid id, RecoveryPerson person, RecoveryDirectoryAccount account, RecoveryQuestion question, DateTime now, CancellationToken ct = default);
    Task<bool> BeginAnswerAsync(Guid id, DateTime now, CancellationToken ct = default);
    Task FinishAnswerAsync(Guid id, bool verified, CancellationToken ct = default);
    Task<bool> ClaimResetAsync(Guid id, DateTime now, CancellationToken ct = default);
    Task FinishAsync(Guid id, RecoveryPasswordWriteResult outcome, CancellationToken ct = default);
    Task RejectAsync(Guid id, CancellationToken ct = default);
}
