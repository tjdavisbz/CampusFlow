using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.ClearanceResets;

public interface IClearanceResetProtection
{
    Task<IAsyncDisposable> AcquireAsync(int studentUid,int termId,CancellationToken ct=default);
    Task<HashSet<(int StudentUid,int TermId)>> ProtectedAsync(IReadOnlyCollection<ClearanceResetCandidate> candidates,CancellationToken ct=default);
    Task<bool> IsProtectedAsync(ClearanceResetCandidate candidate,CancellationToken ct=default);
}
public sealed record ClearanceResetTerm(int TermId,string Term);
public sealed record ManualClearanceException(int StudentUid,int TermId,string Term,string Reason,Guid CreatedBy,DateTime CreatedAtUtc);

public sealed class ClearanceUpdateBusyException:InvalidOperationException
{public ClearanceUpdateBusyException():base("Clearance is already being updated for this student and term. Try again after it finishes.") {}}
