using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
public class BillApprovalHistoryModel(ICurrentStudentView currentStudentView,
    IRepository<BillApproval, Guid> approvals, ILegacyBillApprovalLookup legacy) : CampusFlowPageModel
{
    public sealed record HistoryRow(string Term, string TermCode, string Source, string Status,
        DateTime? AcceptedAt, string? Note);
    public IReadOnlyList<HistoryRow> Rows { get; private set; } = [];
    public bool HasProfile { get; private set; }

    public async Task OnGetAsync()
    {
        if (CurrentTenant.Id == null) return;
        var profile = await currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile == null || profile.TenantId != CurrentTenant.Id) return;
        HasProfile = true;
        var native = await approvals.GetListAsync(x => x.TenantId == CurrentTenant.Id &&
            x.StudentProfileId == profile.Id, cancellationToken: HttpContext.RequestAborted);
        var imported = profile.Provider == StudentInformationSystemProvider.ThesisElements
            ? await legacy.HistoryAsync(profile.ExternalStudentId, HttpContext.RequestAborted) : [];
        Rows = native.Select(x => new HistoryRow(x.TermName, x.TermCode, "CampusFlow", x.Status.ToString(), x.AcceptedAt, null))
            .Concat(imported.Select(x => new HistoryRow(
                string.IsNullOrWhiteSpace(x.TermName) ? "Unspecified legacy term" : x.TermName,
                x.TermCode, "Previous portal", x.SourceApproved ? "Approved" : "Not approved", x.AcceptTime,
                x.ExternalTermId == "0" ? "The original record does not identify an academic term." :
                x.SourceApproved && x.AcceptTime == null ? "Approval date is missing; this record needs review before it can count as approved." :
                !x.SourceApproved && x.AcceptTime != null ? "The original status is not approved despite the recorded date." : null)))
            .OrderByDescending(x => x.TermCode).ThenByDescending(x => x.AcceptedAt).ToArray();
    }
}
