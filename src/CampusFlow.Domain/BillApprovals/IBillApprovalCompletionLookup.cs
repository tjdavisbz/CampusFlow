using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Students;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public interface IBillApprovalCompletionLookup
{
    Task<bool> IsApprovedAsync(StudentProfile profile, string externalTermId, CancellationToken cancellationToken = default);
}

public class BillApprovalCompletionLookup(IRepository<BillApproval, Guid> approvals,
    IRepository<LegacyBillApproval, Guid> legacy, ICurrentTenant tenant) : IBillApprovalCompletionLookup, ITransientDependency
{
    public async Task<bool> IsApprovedAsync(StudentProfile profile, string externalTermId, CancellationToken cancellationToken = default)
    {
        if (!tenant.Id.HasValue || profile.TenantId != tenant.Id || string.IsNullOrWhiteSpace(externalTermId)) return false;
        var approval = await approvals.FindAsync(x => x.TenantId == tenant.Id &&
            x.StudentProfileId == profile.Id && x.ExternalTermId == externalTermId, cancellationToken: cancellationToken);
        // A local draft/reset supersedes historical approval for this term.
        if (approval is not null) return approval.AcceptedAt.HasValue && approval.Status is
            BillApprovalStatus.Approved or BillApprovalStatus.DocumentPending or BillApprovalStatus.Completed;
        if (profile.Provider != StudentInformationSystemProvider.ThesisElements || externalTermId == "0") return false;
        return await legacy.AnyAsync(x => x.TenantId == tenant.Id && x.ExternalStudentId == profile.ExternalStudentId &&
            x.ExternalTermId == externalTermId && x.SourceApproved && x.AcceptTime != null, cancellationToken: cancellationToken);
    }
}
