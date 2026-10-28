using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.Chapel;

public sealed record ChapelExcuseApprovalResult(int MatchingSessions, int NewlyExcusedSessions);

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
public class ChapelExcuseReviewAppService : CampusFlowAppService
{
    private readonly IRepository<ChapelExcuseRequest, Guid> _requests;
    private readonly IRepository<ChapelAttendanceRecord, Guid> _attendance;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public ChapelExcuseReviewAppService(
        IRepository<ChapelExcuseRequest, Guid> requests,
        IRepository<ChapelAttendanceRecord, Guid> attendance,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _requests = requests;
        _attendance = attendance;
        _unitOfWorkManager = unitOfWorkManager;
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<ChapelExcuseApprovalResult> ApproveAsync(Guid requestId, string revision)
    {
        var tenantId = CurrentTenant.Id ?? throw new UserFriendlyException("Select a university first.");
        var staffUserId = CurrentUser.Id ?? throw new UserFriendlyException("Your staff identity could not be resolved.");
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var request = await _requests.GetAsync(requestId);
        if (request.TenantId != tenantId) throw new UserFriendlyException("Excuse request not found.");
        if (request.ConcurrencyStamp != revision)
            throw new UserFriendlyException("This request changed. Reload it before approving.");
        if (request.Status != ChapelExcuseRequestStatus.Pending)
            throw new UserFriendlyException("This request has already been approved.");

        var start = request.StartDate.ToDateTime(TimeOnly.MinValue);
        var endExclusive = request.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = (await _attendance.GetQueryableAsync()).Where(x =>
            x.TenantId == tenantId && x.TermCalendarId == request.TermCalendarId &&
            x.StudentUid == request.StudentUid && x.ChapelAtLocal >= start && x.ChapelAtLocal < endExclusive);
        var matching = await AsyncExecuter.ToListAsync(query);
        if (matching.Count == 0)
            throw new UserFriendlyException("No chapel attendance sessions match this request. Check the dates and student before approving.");

        var changed = 0;
        foreach (var record in matching)
        {
            if (!record.ExcuseForApprovedRequest(request.Id)) continue;
            await _attendance.UpdateAsync(record);
            changed++;
        }

        request.Approve(staffUserId, Clock.Now, changed);
        await _requests.UpdateAsync(request);
        await uow.CompleteAsync();
        return new ChapelExcuseApprovalResult(matching.Count, changed);
    }
}
