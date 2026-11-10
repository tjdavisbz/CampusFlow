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
    private sealed class Writer(Func<HousingApplyProgress?> progress):IElementsHousingAssignmentWriter
    {
        public bool Fail;public int? FailAtStudent;public Dictionary<int,(string,string)> RoomNames=[];public List<string> Calls=[];private readonly Dictionary<int,(string,string)> _assigned=[];
        public void EnsureWriteEnabled(){}
        public Task<(string Building,string Room)?> GetAssignedRoomAsync(int studentUid,int periodId,CancellationToken ct=default)=>Task.FromResult(_assigned.TryGetValue(studentUid,out var r)?((string,string)?)r:null);
        public Task VerifyRoomsAsync(int p,IReadOnlyList<HousingWorkspaceRoom> rooms,CancellationToken ct=default)=>Task.CompletedTask;
        private void Claimed(){Assert.Equal("Running",progress()!.Status);Assert.StartsWith("Starting",progress()!.Operations.Last());}
        public Task UpdateCapacitiesAsync(int p,IReadOnlyList<HousingCapacityChange> changes,CancellationToken ct=default){Claimed();Calls.Add(changes[0].Capacity>changes[0].PreviousCapacity?"increase":"decrease");return Task.CompletedTask;}
        public Task AssignAsync(int id,int p,int room,CancellationToken ct=default){Claimed();Calls.Add("assign");if(Fail||FailAtStudent==id)throw new InvalidOperationException();_assigned[id]=RoomNames.TryGetValue(room,out var named)?named:room==1?("Bridges Hall","101"):("Commuter",room==2?"000":"001");return Task.CompletedTask;}
        public Task DeallocateAsync(int id,int p,int room,CancellationToken ct=default){Claimed();Calls.Add("remove");_assigned.Remove(id);return Task.CompletedTask;}
    }
}
