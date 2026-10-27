using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.BillApprovals;

public interface ILegacyBillApprovalLookup
{
    Task<LegacyBillApproval?> FindAsync(string studentUid, string termId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LegacyBillApproval>> HistoryAsync(string studentUid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ApprovedStudentIdsAsync(string termId, CancellationToken cancellationToken = default);
}

public sealed class LegacyBillApprovalLookup(IRepository<LegacyBillApproval, Guid> approvals, ICurrentTenant tenant)
    : ILegacyBillApprovalLookup, ITransientDependency
{
    public async Task<LegacyBillApproval?> FindAsync(string studentUid, string termId, CancellationToken cancellationToken = default) =>
        tenant.Id == null ? null : await approvals.FindAsync(x => x.TenantId == tenant.Id &&
            x.ExternalStudentId == studentUid && x.ExternalTermId == termId, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<LegacyBillApproval>> HistoryAsync(string studentUid, CancellationToken cancellationToken = default) =>
        tenant.Id == null ? [] : await approvals.GetListAsync(x => x.TenantId == tenant.Id &&
            x.ExternalStudentId == studentUid, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<string>> ApprovedStudentIdsAsync(string termId, CancellationToken cancellationToken = default)
    {
        if (tenant.Id == null || termId == "0") return [];
        return (await approvals.GetListAsync(x => x.TenantId == tenant.Id && x.ExternalTermId == termId &&
            x.SourceApproved && x.AcceptTime != null, cancellationToken: cancellationToken))
            .Select(x => x.ExternalStudentId).Distinct().ToArray();
    }
}
