using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Uow;

namespace CampusFlow.Housing;

public partial class HousingWorkspaceAppService
{
    public async Task<HousingRecoveryPreview> PreviewRecoveryAsync(int periodId)
    {
        var draft = await Find(periodId) ?? throw new UserFriendlyException("There is no saved draft to recover.");
        var review = await PrepareRecoveryAsync(draft);
        var expected = review.Current.SelectMany(room => room.Occupants.Select(o => (room, o)))
            .ToDictionary(x => x.o.StudentUid);
        var added = Read(draft.RoomsJson).SelectMany(x => x.Occupants).Select(x => x.StudentUid)
            .Except(Read(draft.BaselineJson).SelectMany(x => x.Occupants).Select(x => x.StudentUid));
        foreach (var id in added)
        {
            var live = await _writer.GetAssignedRoomAsync(id, periodId);
            if (expected.TryGetValue(id, out var row) ? live is null || !SameRoom(live.Value, row.room) : live is not null)
                throw new UserFriendlyException("The live API and housing snapshot disagree. No recovery changes were made; review again after Elements has settled.");
        }
        return review.Preview;
    }

    [Audited]
    [UnitOfWork(IsDisabled = true)]
    public Task<HousingWorkspaceState> ResumeCommuterAssignmentsAsync(RecoverHousingDraftInput input)
    {
        if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 500 ||
            input.RunId == Guid.Empty || string.IsNullOrWhiteSpace(input.RecoveryRevision))
            throw new UserFriendlyException("Review the recovery preview, confirm the live assignments and housing batch, and enter a reason (maximum 500 characters).");
        return ApplyCoreAsync(new ApplyHousingDraftInput
        {
            PeriodId = input.PeriodId, Revision = input.Revision, Confirmed = true, RateRevision = ""
        }, input);
    }

    private async Task<(HousingRecoveryPreview Preview, List<HousingWorkspaceRoom> Current, List<int> Remaining)>
        PrepareRecoveryAsync(HousingAssignmentDraft draft)
    {
        _writer.EnsureWriteEnabled();
        var progress = HousingApplyProgress.Read(draft);
        if (progress?.Status != "Needs review" || progress.FinishedAt is null ||
            progress.BeforeJson != draft.BaselineJson || progress.DesiredJson != draft.RoomsJson)
            throw new UserFriendlyException("Recovery requires a stopped partial apply and its unchanged saved plan. A running or replaced plan cannot be resumed.");
        var before = Read(draft.BaselineJson);
        var desired = Read(draft.RoomsJson);
        var current = await _elements.GetRoomsAsync(draft.PeriodId);
        (List<int> Added, List<int> Remaining) plan;
        try { plan = HousingCommuterRecovery.Review(before, desired, current); }
        catch (ArgumentException error) { throw new UserFriendlyException(error.Message); }
        var identity = new
        {
            Tenant = Tenant(), User = CurrentUser.Id, draft.Id, draft.PeriodId, draft.ConcurrencyStamp,
            progress.RunId, draft.BaselineJson, draft.RoomsJson,
            Rooms = current.OrderBy(x => x.Id).Select(room => new
            {
                room.Id, room.Building, room.Name, room.Capacity,
                Students = room.Occupants.Select(x => x.StudentUid).Order().ToArray()
            }).ToArray()
        };
        return (new HousingRecoveryPreview
        {
            PeriodId = draft.PeriodId, RunId = progress.RunId, Revision = draft.ConcurrencyStamp,
            RecoveryRevision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity)))),
            SavedAssignments = desired.Sum(x => x.Occupants.Count), AlreadyAssigned = plan.Added.Count - plan.Remaining.Count,
            RemainingAssignments = plan.Remaining.Count, Failure = HousingApplyFailure.Description(progress.FailureCode)
        }, current, plan.Remaining);
    }
}
