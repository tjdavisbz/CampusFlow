using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.BillApprovals;

public interface IBillApprovalCourseReadiness
{
    Task<bool> HasPendingAsync(Guid studentProfileId, string externalStudentId, string externalTermId,
        CancellationToken cancellationToken = default);
}
