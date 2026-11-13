using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.Application.Tests.Housing;
public class HousingApplyTests
{
    private readonly Guid _tenant=Guid.NewGuid();
    private readonly HousingAssignmentDraft _draft;
    private readonly IElementsHousingReader _reader=Substitute.For<IElementsHousingReader>();
    private readonly IElementsHousingBillingWriter _billing=Substitute.For<IElementsHousingBillingWriter>();
    private readonly IRepository<HousingRateSchedule,Guid> _rates=Substitute.For<IRepository<HousingRateSchedule,Guid>>();
    private readonly HousingRateSchedule _schedule;
    private readonly Writer _writer;
    private readonly HousingWorkspaceAppService _service;
    private readonly List<HousingWorkspaceRoom> _before=[new(){Id=1,Name="101",Building="Bridges Hall",Capacity=2},new(){Id=2,Name="000",Building="Commuter",Capacity=10},new(){Id=3,Name="001",Building="Commuter",Capacity=10}];
    public HousingApplyTests()
    {
        var now=new DateTime(2026,11,5,12,0,0,DateTimeKind.Utc);
        _draft=new(Guid.NewGuid(),_tenant,1,JsonSerializer.Serialize(_before),now);
        var drafts=Substitute.For<IRepository<HousingAssignmentDraft,Guid>>();drafts.FindAsync(Arg.Any<Expression<Func<HousingAssignmentDraft,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(_draft);
        _schedule=new(Guid.NewGuid(),_tenant,1,268,JsonSerializer.Serialize(new[]{new HousingRateAmount("Bridges Hall","Standard",2500,2125)}));
        _rates.FindAsync(Arg.Any<Expression<Func<HousingRateSchedule,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(_schedule);
        _reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_before);
        _reader.GetPricingAsync(1,Arg.Any<CancellationToken>()).Returns(new HousingPricingContext(268,[new("Bridges/Teeter",137,2500)],243));
        var students=Substitute.For<IStudentInformationSystemStudentLookup>();students.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Housing:EnableNonProductionWrites"]="true"}).Build();
        var lazy=Substitute.For<IAbpLazyServiceProvider>();var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(_tenant);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var clock=Substitute.For<IClock>();clock.Now.Returns(now);lazy.LazyGetRequiredService<IClock>().Returns(clock);
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        _writer=new(()=>HousingApplyProgress.Read(_draft));
        _service=new(drafts,_rates,_reader,_writer,_billing,config,Substitute.For<IUnitOfWorkManager>(),[students]){LazyServiceProvider=lazy};
    }
    [Fact] public async Task Period_setup_is_read_only_and_does_not_require_or_change_a_draft()
    {
        var setup=new HousingPeriodSetup {PeriodId=2,PeriodName="Spring 2027",MasterCommuterRoomCount=20,PeriodCommuterRoomCount=1,MissingCommuterRooms=["Commuter / 2"]};
        _reader.GetPeriodSetupAsync(2,Arg.Any<CancellationToken>()).Returns(setup);
        var before=_draft.RoomsJson;
        Assert.Same(setup,await _service.GetPeriodSetupAsync(2));
        Assert.Equal(before,_draft.RoomsJson);
        Assert.Empty(_billing.ReceivedCalls());
        await _reader.DidNotReceiveWithAnyArgs().GetRoomsAsync(default,default);
    }
    private void Desired(Action<List<HousingWorkspaceRoom>> change)
    {var rooms=JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.BaselineJson)!;change(rooms);_draft.Save(JsonSerializer.Serialize(rooms));}
    private ApplyHousingDraftInput Input(bool fees=true)=>new(){PeriodId=1,Revision=_draft.ConcurrencyStamp,RateRevision=fees?_schedule.ConcurrencyStamp:"",Confirmed=true};
    [Fact]public async Task Notes_do_not_block_assignment_and_single_charge_remains_separate()
    {
        Desired(x=>x[0].Occupants.Add(new(){StudentUid=10,Name="Test",Rate="Standard",IntentionalSingle=true,Notes="Staff note"}));
        var result=await _service.ApplyAssignmentsAsync(Input());
        await _billing.Received(1).ReconcileAsync(10,268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==2&&x[0].Amount==2500&&x[1].Amount==2125),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>());
        Assert.Equal("Staff note",result.Rooms[0].Occupants[0].Notes);Assert.Equal("Completed",result.ApplyStatus);
    }
    [Fact]public async Task Capacity_only_apply_needs_no_fee_schedule_and_writes_no_billing()
    {
        Desired(x=>x[0].Capacity=3);_rates.FindAsync(Arg.Any<Expression<Func<HousingRateSchedule,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns((HousingRateSchedule?)null);
        Assert.Equal("",await _service.GetRateRevisionAsync(1));var result=await _service.ApplyAssignmentsAsync(Input(false));
        Assert.Equal(3,result.Rooms[0].Capacity);Assert.Equal("increase",Assert.Single(_writer.Calls));
        await _reader.DidNotReceiveWithAnyArgs().GetPricingAsync(default);await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
    }
    [Fact]public async Task Commuter_assignment_needs_no_rate_mapping_and_has_no_charges()
    {
        Desired(x=>x[1].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));_rates.FindAsync(Arg.Any<Expression<Func<HousingRateSchedule,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns((HousingRateSchedule?)null);
        await _service.ApplyAssignmentsAsync(Input(false));
        await _billing.Received(1).ReconcileAsync(10,268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==0),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Multiple_commuter_rooms_each_receive_assignments_without_housing_charges()
    {
        Desired(x=>{x[1].Occupants.Add(new(){StudentUid=10,Rate="Standard"});x[2].Occupants.Add(new(){StudentUid=11,Rate="Standard"});});
        await _service.ApplyAssignmentsAsync(Input(false));
        await _billing.Received(2).ReconcileAsync(Arg.Any<int>(),268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==0),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Capacity_is_increased_before_assigning_the_student()
    {Desired(x=>{x[0].Capacity=3;x[0].Occupants.Add(new(){StudentUid=10,Rate="Standard"});});await _service.ApplyAssignmentsAsync(Input());Assert.Equal(new[]{"increase","assign"},_writer.Calls);}
    [Fact]public async Task Failure_is_recorded_before_any_retry_can_occur()
    {
        Desired(x=>x[0].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));_writer.Fail=true;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input()));
        var progress=HousingApplyProgress.Read(_draft)!;Assert.Equal("Needs review",progress.Status);Assert.Contains(progress.Operations,x=>x=="Starting room assignment for 10");
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input()));Assert.Single(_writer.Calls);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
    }
    [Fact]public async Task A_success_envelope_without_a_confirmed_room_never_reaches_billing()
    {
        Desired(x=>x[1].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));_writer.SuppressAssignment=true;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));
        Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
    }
    [Fact]public async Task Concurrent_draft_change_before_claim_prevents_all_writes()
    {
        Desired(x=>x[0].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));var input=Input();
        _reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_=>{_draft.ConcurrencyStamp=Guid.NewGuid().ToString();return _before;});
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(input));Assert.Empty(_writer.Calls);
    }
    [Fact]public async Task Fee_revision_change_prevents_all_writes()
    {Desired(x=>x[0].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));var input=Input();input.RateRevision="stale";await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(input));Assert.Empty(_writer.Calls);}
    private void SavedCommuterBatch()
    {
        _before.Clear();
        for(var id=1;id<=20;id++)
        {
            var room=new HousingWorkspaceRoom{Id=id,Name=$"Commuter {id}",Building="Commuter",Capacity=10};
            _before.Add(room);_writer.RoomNames[id]=(room.Building,room.Name);
        }
        _draft.Refresh(JsonSerializer.Serialize(_before),DateTime.UtcNow);
        Desired(rooms=>{for(var id=1;id<=113;id++)rooms[(id-1)%20].Occupants.Add(new(){StudentUid=id,Name=$"Test {id}",Rate="Standard"});});
    }
    [Fact]public async Task All_113_saved_commuter_assignments_survive_successful_apply_without_housing_charges()
    {
        SavedCommuterBatch();var result=await _service.ApplyAssignmentsAsync(Input(false));
        Assert.Equal(113,result.Rooms.Sum(x=>x.Occupants.Count));Assert.Equal(113,JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.BaselineJson)!.Sum(x=>x.Occupants.Count));
        Assert.Equal(113,_writer.Calls.Count(x=>x=="assign"));Assert.Equal("Completed",result.ApplyStatus);
        await _billing.Received(113).ReconcileAsync(Arg.Any<int>(),268,Arg.Is<IReadOnlyList<HousingChargeLine>>(x=>x.Count==0),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Partial_failure_preserves_all_113_draft_entries_and_blocks_a_blind_retry()
    {
        SavedCommuterBatch();_writer.FailAtStudent=60;var desired=_draft.RoomsJson;var baseline=_draft.BaselineJson;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));
        Assert.Equal(desired,_draft.RoomsJson);Assert.Equal(baseline,_draft.BaselineJson);Assert.Equal(113,JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.RoomsJson)!.Sum(x=>x.Occupants.Count));
        Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);Assert.Equal(60,_writer.Calls.Count(x=>x=="assign"));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));Assert.Equal(60,_writer.Calls.Count(x=>x=="assign"));
    }

    private void ReplicaTracksWriter() => _reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_ =>
    {
        var rooms=JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(_before))!;
        foreach(var (id,identity) in _writer.Assigned)
            rooms.Single(x=>x.Building==identity.Item1&&x.Name==identity.Item2).Occupants.Add(new(){StudentUid=id,Rate="Standard"});
        return rooms;
    });
    private async Task<HousingRecoveryPreview> FailedCommuterRun()
    {
        SavedCommuterBatch();_writer.FailAtStudent=2;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));
        ReplicaTracksWriter();_writer.FailAtStudent=null;
        return await _service.PreviewRecoveryAsync(1);
    }
    private static RecoverHousingDraftInput Recover(HousingRecoveryPreview preview)=>new()
    {PeriodId=preview.PeriodId,RunId=preview.RunId,Revision=preview.Revision,RecoveryRevision=preview.RecoveryRevision,Confirmed=true,Reason="Reviewed the live rooms and housing batch"};
    [Fact] public async Task Reviewed_deleted_history_is_acknowledged_before_assignment_with_all_113_preserved()
    {
        await FailedCommuterRun();
        _writer.Historical.Add(new(900,2,1,90,80,70,"Commuter","Old",true,true,true,null,null));
        var preview=await _service.PreviewRecoveryAsync(1);Assert.Equal(1,preview.HistoricalAssignmentsToClear);
        _writer.Calls.Clear();var desired=_draft.RoomsJson;
        await _service.ResumeCommuterAssignmentsAsync(Recover(preview));
        Assert.Equal("historical",_writer.Calls[0]);Assert.Equal("assign",_writer.Calls[1]);
        Assert.Equal(112,_writer.Calls.Count(x=>x=="assign"));Assert.Equal(desired,_draft.RoomsJson);
        Assert.Equal(113,JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.RoomsJson)!.Sum(x=>x.Occupants.Count));
        Assert.Contains(HousingApplyProgress.Read(_draft)!.Operations,x=>x.StartsWith("Starting historical room deallocation"));
    }
    [Fact] public async Task Changed_historical_metadata_invalidates_the_reviewed_recovery_token()
    {
        var preview=await FailedCommuterRun();
        _writer.Historical.Add(new(900,2,1,90,80,70,"Commuter","Old",true,true,true,null,null));_writer.Calls.Clear();
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));
        Assert.Empty(_writer.Calls);
    }
    [Fact] public async Task Unconfirmed_historical_deallocation_stops_before_the_next_assignment_and_preserves_plan()
    {
        await FailedCommuterRun();_writer.Historical.Add(new(900,2,1,90,80,70,"Commuter","Old",true,true,true,null,null));
        var preview=await _service.PreviewRecoveryAsync(1);var desired=_draft.RoomsJson;_writer.Calls.Clear();_writer.FailHistorical=true;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));
        Assert.Empty(_writer.Calls);Assert.Equal(desired,_draft.RoomsJson);Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
    }
    [Fact] public async Task Reviewed_recovery_skips_completed_rooms_retains_all_113_and_preserves_all_billing()
    {
        var preview=await FailedCommuterRun();var desired=_draft.RoomsJson;var original=HousingApplyProgress.Read(_draft)!;
        Assert.Equal(113,preview.SavedAssignments);Assert.Equal(1,preview.AlreadyAssigned);Assert.Equal(112,preview.RemainingAssignments);
        Assert.Equal(2,_writer.Calls.Count);_writer.Calls.Clear();
        var result=await _service.ResumeCommuterAssignmentsAsync(Recover(preview));
        Assert.Equal(112,_writer.Calls.Count);Assert.All(_writer.Calls,x=>Assert.Equal("assign",x));
        Assert.Equal(113,result.Rooms.Sum(x=>x.Occupants.Count));Assert.Equal(desired,_draft.RoomsJson);Assert.Equal(desired,_draft.BaselineJson);
        var progress=HousingApplyProgress.Read(_draft)!;Assert.Equal("Completed",progress.Status);Assert.Equal(original.RunId,progress.RecoveryOfRunId);Assert.NotEmpty(progress.ReviewReason!);
        var history=JsonSerializer.Deserialize<List<HousingApplyProgress>>(_draft.ExtraProperties["HousingApplyHistoryV1"]!.ToString()!)!;
        Assert.Equal(original.RunId,Assert.Single(history).RunId);Assert.Equal(original.DesiredJson,history[0].DesiredJson);Assert.Equal(original.Operations,history[0].Operations);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
        Assert.Equal(113,progress.Operations.Count(x=>x.StartsWith("Existing billing preserved")));
    }
    [Theory][InlineData("revision")][InlineData("run")][InlineData("preview")][InlineData("confirmation")][InlineData("reason")][InlineData("reason-length")]
    public async Task Recovery_requires_the_exact_reviewed_run_and_explicit_confirmation(string invalid)
    {
        var preview=await FailedCommuterRun();var input=Recover(preview);_writer.Calls.Clear();var desired=_draft.RoomsJson;
        switch(invalid){case "revision":input.Revision="stale";break;case "run":input.RunId=Guid.NewGuid();break;case "preview":input.RecoveryRevision="wrong";break;case "confirmation":input.Confirmed=false;break;case "reason":input.Reason=" ";break;case "reason-length":input.Reason=new string('x',501);break;}
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(input));Assert.Empty(_writer.Calls);Assert.Equal(desired,_draft.RoomsJson);
    }
    [Fact]public async Task Conflicting_live_assignment_blocks_recovery_without_writes_or_draft_loss()
    {
        var preview=await FailedCommuterRun();_writer.Assigned[2]=_writer.RoomNames[20];_writer.Calls.Clear();var desired=_draft.RoomsJson;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));Assert.Empty(_writer.Calls);Assert.Equal(desired,_draft.RoomsJson);
    }
    [Fact]public async Task Live_api_disagreement_blocks_the_read_only_recovery_preview()
    {
        await FailedCommuterRun();_reader.GetRoomsAsync(1,Arg.Any<CancellationToken>()).Returns(_before);_writer.Calls.Clear();
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.PreviewRecoveryAsync(1));Assert.Empty(_writer.Calls);
    }
    [Fact]public async Task Another_partial_failure_keeps_the_plan_and_requires_a_new_review()
    {
        var preview=await FailedCommuterRun();var desired=_draft.RoomsJson;_writer.FailAtStudent=3;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));
        Assert.Equal(desired,_draft.RoomsJson);Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
        var count=_writer.Calls.Count;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));Assert.Equal(count,_writer.Calls.Count);
        _writer.FailAtStudent=null;var next=await _service.PreviewRecoveryAsync(1);Assert.Equal(2,next.AlreadyAssigned);Assert.Equal(111,next.RemainingAssignments);
        await _service.ResumeCommuterAssignmentsAsync(Recover(next));Assert.Equal("Completed",HousingApplyProgress.Read(_draft)!.Status);
        Assert.Equal(2,JsonSerializer.Deserialize<List<HousingApplyProgress>>(_draft.ExtraProperties["HousingApplyHistoryV1"]!.ToString()!)!.Count);
    }
    [Fact]public async Task All_rooms_already_assigned_preserves_billing_without_duplicate_assignments()
    {
        await FailedCommuterRun();foreach(var room in JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.RoomsJson)!)foreach(var student in room.Occupants)_writer.Assigned[student.StudentUid]=(room.Building,room.Name);
        var preview=await _service.PreviewRecoveryAsync(1);Assert.Equal(0,preview.RemainingAssignments);_writer.Calls.Clear();
        await _service.ResumeCommuterAssignmentsAsync(Recover(preview));Assert.Empty(_writer.Calls);
        await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
    }
    [Fact]public async Task A_room_change_during_recovery_final_verification_stops_without_touching_billing()
    {
        var preview=await FailedCommuterRun();
        _writer.OnRead=id=>{if(id==2&&HousingApplyProgress.Read(_draft)!.Operations.Any(x=>x.StartsWith("Existing billing preserved for 1;")))_writer.Assigned[2]=_writer.RoomNames[20];};
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));
        await _billing.DidNotReceiveWithAnyArgs().ReconcileAsync(default,default,default!,default!);
        Assert.Equal(113,JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.RoomsJson)!.Sum(x=>x.Occupants.Count));Assert.Equal("Needs review",HousingApplyProgress.Read(_draft)!.Status);
    }
    [Fact]public async Task Running_operation_cannot_be_recovered_or_archived()
    {
        var preview=await FailedCommuterRun();var progress=HousingApplyProgress.Read(_draft)!;progress.Status="Running";progress.Save(_draft);_writer.Calls.Clear();
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ResumeCommuterAssignmentsAsync(Recover(preview)));Assert.Empty(_writer.Calls);Assert.False(_draft.ExtraProperties.ContainsKey("HousingApplyHistoryV1"));
    }
    [Fact]public async Task Stale_running_recovery_retains_113_assignments_and_resumes_only_the_remaining_students()
    {
        await FailedCommuterRun();var progress=HousingApplyProgress.Read(_draft)!;
        progress.Status="Running";progress.FinishedAt=null;progress.UpdatedAt=progress.StartedAt.AddMinutes(-31);progress.Save(_draft);
        foreach(var room in JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(_draft.RoomsJson)!)
            foreach(var student in room.Occupants.Where(x=>x.StudentUid<=51))_writer.Assigned[student.StudentUid]=(room.Building,room.Name);
        var desired=_draft.RoomsJson;_writer.Calls.Clear();_billing.ClearReceivedCalls();var state=await _service.GetAsync(1);
        Assert.True(state!.RecoveryAvailable);Assert.Equal("Running",state.ApplyStatus);
        var preview=await _service.PreviewRecoveryAsync(1);Assert.Equal(51,preview.AlreadyAssigned);Assert.Equal(62,preview.RemainingAssignments);
        var result=await _service.ResumeCommuterAssignmentsAsync(Recover(preview));
        Assert.Equal("Completed",result.ApplyStatus);Assert.Equal(desired,_draft.RoomsJson);Assert.Equal(62,_writer.Calls.Count(x=>x=="assign"));
        Assert.Empty(_billing.ReceivedCalls());
    }
    [Fact]public async Task Active_background_dispatch_blocks_a_second_review_but_its_owned_worker_can_resume()
    {
        var preview=await FailedCommuterRun();var user=_service.LazyServiceProvider.LazyGetRequiredService<ICurrentUser>();
        var dispatch=new HousingRecoveryDispatch{Id=Guid.NewGuid(),UserId=user.Id!.Value,StartedAt=HousingApplyProgress.Read(_draft)!.StartedAt};dispatch.Save(_draft);
        Assert.False((await _service.GetAsync(1))!.RecoveryAvailable);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.PreviewRecoveryAsync(1));
        using(HousingRecoveryDispatch.EnterExecution(dispatch.Id))
        {var result=await _service.ResumeCommuterAssignmentsAsync(Recover(preview));Assert.Equal(113,result.Rooms.Sum(x=>x.Occupants.Count));}
    }
    [Fact]public async Task Reviewed_zero_charge_recovery_leaves_posted_and_manual_billing_untouched()
    {
        var preview=await FailedCommuterRun();_writer.Calls.Clear();_billing.ClearReceivedCalls();
        _billing.VerifyCanReconcileAsync(Arg.Any<int>(),Arg.Any<int>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Existing non-CampusFlow housing charge")));
        var result=await _service.ResumeCommuterAssignmentsAsync(Recover(preview));
        Assert.Equal("Completed",result.ApplyStatus);Assert.Equal(112,_writer.Calls.Count);
        Assert.Empty(_billing.ReceivedCalls());Assert.Equal(113,HousingApplyProgress.Read(_draft)!.Operations.Count(x=>x.StartsWith("Existing billing preserved")));
    }
    [Fact]public async Task Normal_apply_keeps_billing_safeguard_and_reports_it_before_claim_or_room_write()
    {
        Desired(x=>x[1].Occupants.Add(new(){StudentUid=10,Rate="Standard"}));var plan=_draft.RoomsJson;
        _billing.VerifyCanReconcileAsync(Arg.Any<int>(),Arg.Any<int>(),Arg.Any<IReadOnlyCollection<int>>(),Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Existing non-CampusFlow housing charge")));
        var error=await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.ApplyAssignmentsAsync(Input(false)));
        Assert.Contains("before any assignment changed",error.Message);Assert.Contains("Existing non-CampusFlow",error.Message);Assert.Empty(_writer.Calls);Assert.Null(HousingApplyProgress.Read(_draft));Assert.Equal(plan,_draft.RoomsJson);
    }

    private sealed class Writer(Func<HousingApplyProgress?> progress):IElementsHousingAssignmentWriter
    {
        public Action<int>? OnRead;public bool Fail;public bool SuppressAssignment;public int? FailAtStudent;public Dictionary<int,(string,string)> RoomNames=[];public List<string> Calls=[];public readonly Dictionary<int,(string,string)> Assigned=[];
        public void EnsureWriteEnabled(){}
        public List<HousingHistoricalAssignment> Historical=[];
        public bool FailHistorical;
        public Task<List<HousingHistoricalAssignment>> GetHistoricalAssignmentsAsync(int period,IReadOnlyCollection<int> ids,CancellationToken ct=default)=>Task.FromResult(Historical.Where(x=>x.PeriodId==period&&ids.Contains(x.StudentUid)).ToList());
        public Task ClearHistoricalAssignmentAsync(HousingHistoricalAssignment row,CancellationToken ct=default)
        {Claimed();if(FailHistorical)throw new InvalidOperationException("Historical deallocation unconfirmed");Calls.Add("historical");return Task.CompletedTask;}
        public Task<(string Building,string Room)?> GetAssignedRoomAsync(int studentUid,int periodId,CancellationToken ct=default){OnRead?.Invoke(studentUid);return Task.FromResult(Assigned.TryGetValue(studentUid,out var r)?((string,string)?)r:null);}
        public Task VerifyRoomsAsync(int p,IReadOnlyList<HousingWorkspaceRoom> rooms,CancellationToken ct=default)=>Task.CompletedTask;
        private void Claimed(){Assert.Equal("Running",progress()!.Status);Assert.StartsWith("Starting",progress()!.Operations.Last());}
        public Task UpdateCapacitiesAsync(int p,IReadOnlyList<HousingCapacityChange> changes,CancellationToken ct=default){Claimed();Calls.Add(changes[0].Capacity>changes[0].PreviousCapacity?"increase":"decrease");return Task.CompletedTask;}
        public Task AssignAsync(int id,int p,int room,CancellationToken ct=default){Claimed();Calls.Add("assign");if(Fail||FailAtStudent==id)throw new InvalidOperationException();if(!SuppressAssignment)Assigned[id]=RoomNames.TryGetValue(room,out var named)?named:room==1?("Bridges Hall","101"):("Commuter",room==2?"000":"001");return Task.CompletedTask;}
        public Task DeallocateAsync(int id,int p,int room,CancellationToken ct=default){Claimed();Calls.Add("remove");Assigned.Remove(id);return Task.CompletedTask;}
    }
}
