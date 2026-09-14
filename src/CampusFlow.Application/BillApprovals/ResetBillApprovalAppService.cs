using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.BillApprovals;

[Authorize(CampusFlowPermissions.Admin.ResetIndividualBillApproval)]
public class ResetBillApprovalAppService : CampusFlowAppService, IResetBillApprovalAppService
{
    private readonly IRepository<BillApproval, Guid> _approvals;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly ILogger<ResetBillApprovalAppService> _logger;

    public ResetBillApprovalAppService(IRepository<BillApproval, Guid> approvals,
        IRepository<StudentProfile, Guid> profiles, ILogger<ResetBillApprovalAppService> logger)
    {
        _approvals = approvals;
        _profiles = profiles;
        _logger = logger;
    }

    public async Task<ResetBillApprovalSearchResultDto> SearchAsync(string? termCode, string? query)
    {
        var approvalQuery = await _approvals.GetQueryableAsync();
        var profileQuery = await _profiles.GetQueryableAsync();
        var normalizedQuery = query?.Trim();

        var terms = await AsyncExecuter.ToListAsync(approvalQuery
            .Select(x => new { x.TermCode, x.TermName })
            .Distinct()
            .OrderByDescending(x => x.TermCode));

        var rows = from approval in approvalQuery
            join profile in profileQuery on approval.StudentProfileId equals profile.Id
            where (string.IsNullOrWhiteSpace(termCode) || approval.TermCode == termCode)
               && (string.IsNullOrWhiteSpace(normalizedQuery)
                   || approval.StudentId.Contains(normalizedQuery)
                   || profile.FirstName.Contains(normalizedQuery)
                   || profile.LastName.Contains(normalizedQuery)
                   || profile.Email.Contains(normalizedQuery))
            orderby approval.TermCode descending, profile.LastName, profile.FirstName
            select new ResetBillApprovalDto
            {
                Id = approval.Id,
                StudentId = approval.StudentId,
                StudentName = profile.PreferredName == null || profile.PreferredName == string.Empty
                    ? profile.FirstName + " " + profile.LastName
                    : profile.PreferredName + " " + profile.LastName,
                TermCode = approval.TermCode,
                TermName = approval.TermName,
                Status = approval.Status.ToString(),
                AcceptedAt = approval.AcceptedAt
            };

        return new ResetBillApprovalSearchResultDto
        {
            Terms = terms.Select(x => new ResetBillApprovalTermDto
            {
                TermCode = x.TermCode,
                TermName = x.TermName
            }).ToList(),
            Approvals = await AsyncExecuter.ToListAsync(rows.Take(100))
        };
    }

    public async Task<ResetBillApprovalDto> GetAsync(Guid id)
    {
        var result = await SearchByIdAsync(id);
        return result ?? throw new UserFriendlyException("That Bill Approval record no longer exists.");
    }

    [Audited]
    public async Task ResetAsync(Guid id)
    {
        var approval = await _approvals.FindAsync(id)
            ?? throw new UserFriendlyException("That Bill Approval record no longer exists. It may already have been reset.");

        _logger.LogWarning(
            "Bill Approval {BillApprovalId} for student {StudentId} and term {TermCode} was reset by user {UserId}.",
            approval.Id, approval.StudentId, approval.TermCode, CurrentUser.Id);
        await _approvals.DeleteAsync(approval, autoSave: true);
    }

    private async Task<ResetBillApprovalDto?> SearchByIdAsync(Guid id)
    {
        var approvalQuery = await _approvals.GetQueryableAsync();
        var profileQuery = await _profiles.GetQueryableAsync();
        var row = from approval in approvalQuery
            join profile in profileQuery on approval.StudentProfileId equals profile.Id
            where approval.Id == id
            select new ResetBillApprovalDto
            {
                Id = approval.Id,
                StudentId = approval.StudentId,
                StudentName = profile.PreferredName == null || profile.PreferredName == string.Empty
                    ? profile.FirstName + " " + profile.LastName
                    : profile.PreferredName + " " + profile.LastName,
                TermCode = approval.TermCode,
                TermName = approval.TermName,
                Status = approval.Status.ToString(),
                AcceptedAt = approval.AcceptedAt
            };
        return await AsyncExecuter.FirstOrDefaultAsync(row);
    }
}
