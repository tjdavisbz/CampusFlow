using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.BillApprovals;

public interface IPaymentPlanFeeService
{
    Task<decimal> ExistingAmountAsync(int studentUid, int termId, decimal expectedFee, CancellationToken ct = default);
    // Caller holds the student/term billing lease. Stable student/term reference
    // prevents duplicate fees across retries, resets and later reapprovals.
    Task EnsureAsync(int studentUid, int termId, decimal expectedFee, CancellationToken ct = default);
}
