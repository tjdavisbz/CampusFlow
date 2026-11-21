using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using NSubstitute;
using Volo.Abp;
using Xunit;
namespace CampusFlow.Application.Tests.Housing;
public partial class HousingApplyTests
{
    private async Task<HousingRecoveryPreview> FailedResidentialBilling()
    {
        _before[0].Occupants.AddRange(new[]{new HousingWorkspaceOccupant{StudentUid=10,Name="First",Rate="Standard"},new(){StudentUid=11,Name="Second",Rate="Standard"}});
        _before.Add(new(){Id=4,Building="Bridges Hall",Name="102",Capacity=2,Occupants=[new(){StudentUid=12,Name="Third",Rate="Standard"}]});
        _writer.RoomNames[1]=("Bridges Hall","101");_writer.RoomNames[4]=("Bridges Hall","102");
        _writer.Assigned[10]=_writer.RoomNames[1];_writer.Assigned[11]=_writer.RoomNames[1];_writer.Assigned[12]=_writer.RoomNames[4];
        _draft.Refresh(JsonSerializer.Serialize(_before),DateTime.UtcNow);
        Desired(rooms=>{var moving=rooms[0].Occupants[0];rooms[0].Occupants.Clear();rooms.Single(r=>r.Id==4).Occupants.Clear();rooms.Single(r=>r.Id==4).Occupants.Add(moving);});
        _reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var rooms=JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(_before))!;
            foreach(var room in rooms) room.Occupants.Clear();
            foreach(var (id,identity) in _writer.Assigned)
                rooms.Single(x=>x.Building==identity.Item1&&x.Name==identity.Item2).Occupants.Add(new(){StudentUid=id,Rate="Standard"});
            return rooms;
        });
        _billing.ReconcileAsync(10,268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(_=>throw new InvalidOperationException("Unrecognized imported charge"));
        var failure=await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input()));
        Assert.True(HousingApplyProgress.Read(_draft) is not null,failure.Message);
        Assert.Equal("billing-review",HousingApplyProgress.Read(_draft)!.FailureCode);
        _billing.ReviewAsync(Arg.Any<int>(),268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(call=>new HousingBillingReview(call.ArgAt<int>(0),268,2500m,call.ArgAt<IReadOnlyList<HousingChargeLine>>(2).Sum(x=>x.Amount),"review-"+call.ArgAt<int>(0)));
        _writer.Calls.Clear();
        return await _service.PreviewRecoveryAsync(1);
    }
    [Fact] public async Task Billing_preview_retains_the_whole_plan_and_shows_the_move_and_both_removals()
    {
        var preview=await FailedResidentialBilling();
        Assert.Equal("billing",preview.Mode);Assert.Equal(0,preview.RemainingAssignments);Assert.Equal(3,preview.BillingChanges.Count);
        Assert.Equal(2500m,preview.BillingChanges.Single(x=>x.StudentUid==10).ProposedAmount);
        Assert.All(preview.BillingChanges.Where(x=>x.StudentUid!=10),x=>Assert.Equal(0,x.ProposedAmount));
        Assert.Empty(_writer.Calls);await _billing.DidNotReceiveWithAnyArgs().ReconcileReviewedAsync(default,default,default!,default!,default!);
    }
    [Fact] public async Task Billing_recovery_finishes_charges_without_repeating_any_room_write()
    {
        var preview=await FailedResidentialBilling();var desired=_draft.RoomsJson;var oldRun=HousingApplyProgress.Read(_draft)!.RunId;
        var result=await _service.ResumeBillingReconciliationAsync(Recover(preview));
        Assert.Empty(_writer.Calls);Assert.Equal("Completed",result.ApplyStatus);Assert.Equal(desired,_draft.RoomsJson);Assert.Equal(desired,_draft.BaselineJson);
        Assert.Equal(oldRun,HousingApplyProgress.Read(_draft)!.RecoveryOfRunId);Assert.Equal("billing",HousingApplyProgress.Read(_draft)!.RecoveryMode);
        await _billing.Received(3).ReconcileReviewedAsync(Arg.Any<int>(),268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
    }
    [Fact] public async Task Changed_charge_review_stops_before_claim_or_writes()
    {
        var preview=await FailedResidentialBilling();var run=HousingApplyProgress.Read(_draft)!.RunId;
        _billing.ReviewAsync(10,268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(new HousingBillingReview(10,268,2600,2500,"changed"));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeBillingReconciliationAsync(Recover(preview)));
        Assert.Empty(_writer.Calls);Assert.Equal(run,HousingApplyProgress.Read(_draft)!.RunId);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileReviewedAsync(default,default,default!,default!,default!);
    }
    [Fact] public async Task A_live_room_change_stops_billing_recovery_without_discarding_the_plan()
    {
        var preview=await FailedResidentialBilling();var desired=_draft.RoomsJson;_writer.Assigned[10]=_writer.RoomNames[1];
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeBillingReconciliationAsync(Recover(preview)));
        Assert.Empty(_writer.Calls);Assert.Equal(desired,_draft.RoomsJson);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileReviewedAsync(default,default,default!,default!,default!);
    }
    [Fact] public async Task Billing_cannot_be_submitted_as_zero_charge_commuter_recovery()
    {
        var preview=await FailedResidentialBilling();await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));Assert.Empty(_writer.Calls);
    }
    [Fact] public async Task Partial_billing_retry_keeps_original_room_proof_and_requires_a_new_review()
    {
        var preview=await FailedResidentialBilling();var fail=true;
        _billing.ReconcileReviewedAsync(11,268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(_=>fail?throw new InvalidOperationException("Interrupted billing"):Task.CompletedTask);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeBillingReconciliationAsync(Recover(preview)));
        Assert.Empty(_writer.Calls);Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeBillingReconciliationAsync(Recover(preview)));
        fail=false;var reviewed=await _service.PreviewRecoveryAsync(1);await _service.ResumeBillingReconciliationAsync(Recover(reviewed));
        Assert.Empty(_writer.Calls);Assert.Equal("Completed",HousingApplyProgress.Read(_draft)!.Status);
    }
}
