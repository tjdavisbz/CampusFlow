using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.BillApprovals;

// Unofficial registrations can already be approved. Only active registrations with
// an outstanding CampusFlow scheduler decision block approval, even after selection closes.
public sealed class BillApprovalCourseReadiness(IRepository<CourseReview, Guid> reviews,
    IStudentInformationSystemCourseSelectionLookup courses) : IBillApprovalCourseReadiness, ITransientDependency
{
    public async Task<bool> HasPendingAsync(Guid studentProfileId, string externalStudentId, string externalTermId,
        CancellationToken cancellationToken = default)
    {
        var active = (await courses.GetRegistrationsAsync(externalStudentId, externalTermId, cancellationToken))
            .Where(x => x.EffectiveWithdrawDate is null).Select(x => x.ExternalRegistrationId).ToArray();
        return active.Length > 0 && await reviews.AnyAsync(x => x.StudentProfileId == studentProfileId &&
            x.ExternalTermId == externalTermId && x.NeedsReview && active.Contains(x.ExternalCourseRegistrationId), cancellationToken);
    }
}
