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
    private async Task<HousingRecoveryPreview> SixInterruptedRemovals()
    {
        _before[0].Capacity=10;
        foreach(var id in Enumerable.Range(10,6))_before[0].Occupants.Add(new(){StudentUid=id,Name="Student "+id,Rate="Standard"});
        _writer.RoomNames[1]=("Bridges Hall","101");
        _writer.Assigned[14]=_writer.RoomNames[1];_writer.Assigned[15]=_writer.RoomNames[1];
        _draft.Refresh(JsonSerializer.Serialize(_before),DateTime.UtcNow);Desired(x=>x[0].Occupants.Clear());
        new HousingApplyProgress{RunId=Guid.NewGuid(),Status="Needs review",StartedAt=DateTime.UtcNow.AddHours(-2),FinishedAt=DateTime.UtcNow.AddHours(-1),BeforeJson=_draft.BaselineJson,DesiredJson=_draft.RoomsJson,
            Operations=["Starting room removal for 10","Room removal verified for 10","Starting room removal for 11","Room removal verified for 11","Starting room removal for 12","Room removal verified for 12","Starting room removal for 13"]}.Save(_draft);
        _reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_=>
        {
            var rooms=JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(_before))!;
            foreach(var r in rooms)r.Occupants.Clear();
            foreach(var (id,identity) in _writer.Assigned)rooms.Single(r=>r.Building==identity.Item1&&r.Name==identity.Item2).Occupants.Add(new(){StudentUid=id,Rate="Standard"});return rooms;
        });
        _billing.ReviewAsync(Arg.Any<int>(),268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(c=>new HousingBillingReview(c.ArgAt<int>(0),268,2500,0,"charge-"+c.ArgAt<int>(0)));
        return await _service.PreviewRecoveryAsync(1);
    }
    [Fact]public async Task Partial_preview_shows_two_removals_and_six_charges_without_writes()
    {
        var preview=await SixInterruptedRemovals();Assert.Equal("partial",preview.Mode);Assert.Equal(2,preview.RemainingRemovals);Assert.Equal(0,preview.RemainingAssignments);Assert.Equal(2,preview.RoomChanges.Count);Assert.Equal(6,preview.BillingChanges.Count);Assert.All(preview.BillingChanges,x=>Assert.Equal(0,x.ProposedAmount));Assert.Empty(_writer.Calls);
    }
    [Fact]public async Task Partial_recovery_skips_four_completed_removals_and_reconciles_all_six_charges()
    {
        var preview=await SixInterruptedRemovals();var desired=_draft.RoomsJson;var run=HousingApplyProgress.Read(_draft)!.RunId;
        var result=await _service.ResumePartialAssignmentsAsync(Recover(preview));Assert.Equal("Completed",result.ApplyStatus);Assert.Equal(2,_writer.Calls.Count(x=>x=="remove"));Assert.DoesNotContain("assign",_writer.Calls);Assert.Equal(desired,_draft.BaselineJson);Assert.Equal(run,HousingApplyProgress.Read(_draft)!.RecoveryOfRunId);
        await _billing.Received(6).ReconcileReviewedAsync(Arg.Any<int>(),268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==0),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Partial_recovery_refuses_a_stale_charge_preview_before_any_room_write()
    {
        var preview=await SixInterruptedRemovals();_billing.ReviewAsync(10,268,Arg.Any<IReadOnlyList<HousingChargeLine>>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(new HousingBillingReview(10,268,2000,0,"changed"));await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumePartialAssignmentsAsync(Recover(preview)));Assert.Empty(_writer.Calls);
    }
    [Fact]public async Task A_second_partial_attempt_requires_new_review_and_keeps_prior_completion_proof()
    {
        var preview=await SixInterruptedRemovals();_writer.FailRemovalAtStudent=15;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumePartialAssignmentsAsync(Recover(preview)));Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
        _writer.FailRemovalAtStudent=null;_writer.Calls.Clear();var next=await _service.PreviewRecoveryAsync(1);Assert.Equal(1,next.RemainingRemovals);await _service.ResumePartialAssignmentsAsync(Recover(next));Assert.Single(_writer.Calls);Assert.Equal("Completed",HousingApplyProgress.Read(_draft)!.Status);
    }
    [Fact]public async Task All_141_removals_finish_with_one_verified_charge_reconciliation_per_student()
    {
        _before.Clear();
        for(var room=1;room<=15;room++)_before.Add(new(){Id=room,Building="Bridges Hall",Name=room.ToString(),Capacity=10});
        for(var id=1;id<=141;id++){var room=_before[(id-1)/10];room.Occupants.Add(new(){StudentUid=id,Rate="Standard"});_writer.Assigned[id]=(room.Building,room.Name);}
        _draft.Refresh(JsonSerializer.Serialize(_before),DateTime.UtcNow);Desired(rooms=>rooms.ForEach(r=>r.Occupants.Clear()));
        var result=await _service.ApplyAssignmentsAsync(Input(false));Assert.Equal("Completed",result.ApplyStatus);Assert.Equal(141,_writer.Calls.Count(x=>x=="remove"));Assert.Empty(_writer.Assigned);Assert.All(result.Rooms,x=>Assert.Empty(x.Occupants));
        await _billing.Received(141).ReconcileAsync(Arg.Any<int>(),268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==0),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Queued_housing_work_blocks_save_refresh_and_duplicate_apply_on_the_server()
    {
        new HousingRecoveryDispatch{Id=Guid.NewGuid(),UserId=Guid.NewGuid(),Status="Queued",Mode="apply",StartedAt=new DateTime(2026,11,5,12,0,0,DateTimeKind.Utc)}.Save(_draft);
        _reader.GetPeriodsAsync(Arg.Any<CancellationToken>()).Returns(new List<HousingPeriodOption>{new(){Id=1,Name="Test period"}});
        var state=(await _service.GetAsync(1))!;Assert.Equal("Queued",state.ApplyStatus);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.SaveAsync(state));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RefreshAsync(1,_draft.ConcurrencyStamp));Assert.Empty(_writer.Calls);
    }
    [Fact]public async Task A_failed_background_preflight_never_displays_the_older_completed_run_as_the_latest_status()
    {
        var started=new DateTime(2026,11,5,12,0,0,DateTimeKind.Utc);
        new HousingApplyProgress{RunId=Guid.NewGuid(),Status="Completed",StartedAt=started.AddHours(-1),FinishedAt=started.AddMinutes(-30)}.Save(_draft);
        new HousingRecoveryDispatch{Id=Guid.NewGuid(),UserId=Guid.NewGuid(),Status="Failed",Mode="apply",StartedAt=started,FinishedAt=started.AddSeconds(2),Message="Elements authentication failed before any assignment changed."}.Save(_draft);
        var state=(await _service.GetAsync(1))!;Assert.Equal("Failed",state.ApplyStatus);Assert.Contains("authentication failed",state.ApplyFailure);Assert.False(state.RecoveryAvailable);
    }
    [Fact]public async Task A_failed_background_apply_that_started_writes_still_requires_review()
    {
        var started=new DateTime(2026,11,5,12,0,0,DateTimeKind.Utc);
        new HousingApplyProgress{RunId=Guid.NewGuid(),Status="Needs review",StartedAt=started,FinishedAt=started.AddSeconds(2),FailureCode="unconfirmed"}.Save(_draft);
        new HousingRecoveryDispatch{Id=Guid.NewGuid(),UserId=Guid.NewGuid(),Status="Failed",Mode="apply",StartedAt=started,FinishedAt=started.AddSeconds(2)}.Save(_draft);
        var state=(await _service.GetAsync(1))!;Assert.Equal("Needs review",state.ApplyStatus);Assert.True(state.RecoveryAvailable);
    }
}
