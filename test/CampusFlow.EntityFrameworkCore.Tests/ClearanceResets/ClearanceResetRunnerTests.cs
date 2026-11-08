using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.ClearanceResets;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class ClearanceResetRunnerTests:CampusFlowEntityFrameworkCoreTestBase
{
    private readonly IClearanceResetElements _elements=Substitute.For<IClearanceResetElements>();private readonly IClock _clock=Substitute.For<IClock>();
    private static readonly DateTime Now=new(2026,11,7,20,0,0,DateTimeKind.Utc);
    private IRepository<ClearanceResetConfiguration,Guid> Configs=>GetRequiredService<IRepository<ClearanceResetConfiguration,Guid>>();
    private IRepository<ClearanceResetRun,Guid> Runs=>GetRequiredService<IRepository<ClearanceResetRun,Guid>>();
    private ICurrentTenant Tenant=>GetRequiredService<ICurrentTenant>();private IUnitOfWorkManager Units=>GetRequiredService<IUnitOfWorkManager>();
    private ClearanceResetRunner Runner(){_clock.Now.Returns(Now);return new(Configs,Runs,_elements,Units,Tenant,GetRequiredService<IGuidGenerator>(),_clock);}
    private async Task<Guid> Configure(bool enabled=true,DateTime? when=null)
    {var id=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>{var c=new ClearanceResetConfiguration(id,Tenant.Id!.Value);var s=c.Schedule();s.Configure(enabled,Guid.NewGuid(),when??Now.AddMinutes(-15));c.Save(s);await Configs.InsertAsync(c,true);});return id;}
    private ClearanceResetAppService Service(Guid? actor)
    {var lazy=Substitute.For<IAbpLazyServiceProvider>();var user=Substitute.For<ICurrentUser>();user.Id.Returns(actor);_clock.Now.Returns(Now);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(Tenant);lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<IClock>().Returns(_clock);lazy.LazyGetRequiredService<IGuidGenerator>().Returns(GetRequiredService<IGuidGenerator>());lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());return new(Configs,Runs,_elements,Units,Runner()){LazyServiceProvider=lazy};}
    private static ClearanceResetCandidate Row(int id=10)=>new(id,269,id+1000,"Spring 2027","B27C",1100);
    private void Candidates(int count=1)
    {
        _elements.FindCandidatesAsync(Arg.Any<CancellationToken>()).Returns(Enumerable.Range(1,count).Select(x=>Row(x)).ToList());
        _elements.ResetIfEligibleAsync(Arg.Any<ClearanceResetCandidate>(),Arg.Any<Func<ClearanceResetAttempt,Task<bool>>>(),Arg.Any<CancellationToken>()).Returns(async call=>
        {var c=call.Arg<ClearanceResetCandidate>();var allowed=await call.Arg<Func<ClearanceResetAttempt,Task<bool>>>()(new(c,"{before}","{desired}"));return new ClearanceResetResult(c.StudentUid,c.TermId,c.Term,allowed?"Verified Not Cleared":"Skipped: off");});
    }
    private async Task Check(Action<ClearanceResetConfiguration,List<ClearanceResetRun>> action)=>await WithUnitOfWorkAsync(async()=>action(Assert.Single(await Configs.GetListAsync()),await Runs.GetListAsync()));
    [Fact]public async Task New_or_disabled_schedules_do_not_contact_Elements()
    {using var t=Tenant.Change(Guid.NewGuid());Assert.False(await Runner().RunIfDueAsync());await Configure(false);Assert.False(await Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().FindCandidatesAsync(default);}
    [Fact]public async Task Enabling_persists_and_waits_fifteen_minutes_without_applying()
    {using var t=Tenant.Change(Guid.NewGuid());var result=await Service(Guid.NewGuid()).SetAsync(new(){Enabled=true});Assert.True(result.Enabled);Assert.Equal(Now.AddMinutes(15),result.NextRunAtUtc);Assert.False(await Runner().RunIfDueAsync());await Check((c,r)=>Assert.Empty(r));}
    [Fact]public async Task Due_run_persists_attempts_before_writes_and_audited_results()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();Candidates();var runner=Runner();Assert.True(await runner.RunIfDueAsync());Assert.False(await runner.RunIfDueAsync());await Check((c,r)=>{var run=Assert.Single(r);Assert.Equal("Completed",run.State);Assert.Contains("before",run.AttemptsJson);Assert.Contains("Verified Not Cleared",run.ResultsJson);Assert.Null(c.RunningRunId);Assert.Equal(Now.AddMinutes(15),c.Schedule().NextRunAtUtc);});}
    [Fact]public async Task No_change_cycle_never_calls_the_mutation_boundary()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();Candidates(0);await Runner().RunIfDueAsync();await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);await Check((c,r)=>Assert.Equal("[]",Assert.Single(r).AttemptsJson));}
    [Fact]public async Task One_cycle_handles_at_most_one_hundred_candidates()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();Candidates(101);await Runner().RunIfDueAsync();await _elements.Received(100).ResetIfEligibleAsync(Arg.Any<ClearanceResetCandidate>(),Arg.Any<Func<ClearanceResetAttempt,Task<bool>>>(),Arg.Any<CancellationToken>());await Check((c,r)=>Assert.Contains("1 additional",Assert.Single(r).Summary));}
    [Fact]public async Task Unconfirmed_update_pauses_and_cannot_be_automatically_retried()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();Candidates(2);_elements.ResetIfEligibleAsync(Arg.Any<ClearanceResetCandidate>(),Arg.Any<Func<ClearanceResetAttempt,Task<bool>>>(),Arg.Any<CancellationToken>()).Returns(Task.FromException<ClearanceResetResult>(new ClearanceResetUnconfirmedException(new Exception("unknown"))));var runner=Runner();await runner.RunIfDueAsync();Assert.False(await runner.RunIfDueAsync());await Check((c,r)=>{Assert.False(c.Schedule().Enabled);Assert.Equal("Needs review",Assert.Single(r).State);});}
    [Fact]public async Task Turning_off_during_a_cycle_stops_the_next_student()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();Candidates(2);_elements.ResetIfEligibleAsync(Arg.Any<ClearanceResetCandidate>(),Arg.Any<Func<ClearanceResetAttempt,Task<bool>>>(),Arg.Any<CancellationToken>()).Returns(async call=>{await Service(Guid.NewGuid()).SetAsync(new(){Enabled=false});var c=call.Arg<ClearanceResetCandidate>();return new ClearanceResetResult(c.StudentUid,c.TermId,c.Term,"Verified Not Cleared");});await Runner().RunIfDueAsync();await _elements.Received(1).ResetIfEligibleAsync(Arg.Any<ClearanceResetCandidate>(),Arg.Any<Func<ClearanceResetAttempt,Task<bool>>>(),Arg.Any<CancellationToken>());await Check((c,r)=>{Assert.Equal("Stopped",Assert.Single(r).State);Assert.False(c.Schedule().Enabled);});}
    [Fact]public async Task Turning_off_is_available_when_Elements_is_disabled()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();_elements.When(x=>x.EnsureRunEnabled()).Do(_=>throw new InvalidOperationException("Disabled"));var result=await Service(Guid.NewGuid()).SetAsync(new(){Enabled=false});Assert.False(result.Enabled);}
    [Fact]public async Task Unconfirmed_history_requires_exact_explicit_review_and_keeps_its_original_outcome()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure(false);var id=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>{var run=new ClearanceResetRun(id,Tenant.Id!.Value);run.Finish("Needs review","Unknown");await Runs.InsertAsync(run,true);});var service=Service(Guid.NewGuid());await Assert.ThrowsAsync<UserFriendlyException>(()=>service.SetAsync(new(){Enabled=true}));await Assert.ThrowsAsync<UserFriendlyException>(()=>service.SetAsync(new(){Enabled=true,ReviewedRunIds=[Guid.NewGuid()]}));Assert.True((await service.SetAsync(new(){Enabled=true,ReviewedRunIds=[id]})).Enabled);await Check((c,r)=>{Assert.True(Assert.Single(r).Reviewed);Assert.Equal("Needs review",r[0].State);});}
    [Fact]public async Task Running_claim_remains_blocked_across_restart_and_cannot_be_acknowledged()
    {using var t=Tenant.Change(Guid.NewGuid());await Configure();var id=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>{var c=Assert.Single(await Configs.GetListAsync());c.SetRun(id);await Configs.UpdateAsync(c,true);await Runs.InsertAsync(new(id,Tenant.Id!.Value),true);});Assert.False(await Runner().RunIfDueAsync());await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(Guid.NewGuid()).SetAsync(new(){Enabled=true,ReviewedRunIds=[id]}));}
    [Fact]public async Task Competing_database_claim_cannot_overwrite_the_winner()
    {using var t=Tenant.Change(Guid.NewGuid());var id=await Configure();var winner=Guid.NewGuid();using(var stale=Units.Begin(requiresNew:true,isTransactional:false)){var old=await Configs.GetAsync(id);using(var fresh=Units.Begin(requiresNew:true,isTransactional:false)){var c=await Configs.GetAsync(id);c.SetRun(winner);await Configs.UpdateAsync(c,true);await fresh.CompleteAsync();}old.SetRun(Guid.NewGuid());await Assert.ThrowsAsync<AbpDbConcurrencyException>(()=>Configs.UpdateAsync(old,true));}await Check((c,r)=>Assert.Equal(winner,c.RunningRunId));}
    [Fact]public async Task Host_context_and_missing_actor_cannot_start_or_enable_a_job()
    {using var host=Tenant.Change(null);Assert.False(await Runner().RunIfDueAsync());using var tenant=Tenant.Change(Guid.NewGuid());await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(null).SetAsync(new(){Enabled=true}));}
    [Fact]public void New_services_resolve_in_the_application_container()
    {Assert.NotNull(GetRequiredService<IClearanceResetAppService>());Assert.NotNull(GetRequiredService<IClearanceResetElements>());Assert.NotNull(GetRequiredService<ClearanceResetRunner>());}
    private async Task<ClearanceResetPreviewDto> Preview(ClearanceResetAppService service,int? uid=null)
    {return await WithUnitOfWorkAsync(()=>service.PreviewAsync(uid));}
    [Fact]public async Task Manual_preview_is_read_only_for_Elements_and_does_not_enable_the_schedule()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates(2);var p=await Preview(Service(Guid.NewGuid()));Assert.NotNull(p.RunId);Assert.Equal(2,p.Rows.Count);await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);await WithUnitOfWorkAsync(async()=>{Assert.Empty(await Configs.GetListAsync());Assert.Equal("Preview",Assert.Single(await Runs.GetListAsync()).State);});}
    [Fact]public async Task Reviewed_manual_run_works_with_automation_off_and_is_single_use()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates();var service=Service(Guid.NewGuid());var p=await Preview(service);var results=await service.RunAsync(new(){RunId=p.RunId!.Value});Assert.Equal("Verified Not Cleared",Assert.Single(results).Outcome);await Assert.ThrowsAsync<UserFriendlyException>(()=>service.RunAsync(new(){RunId=p.RunId.Value}));await Check((c,r)=>{Assert.False(c.Schedule().Enabled);Assert.Null(c.RunningRunId);Assert.False(Assert.Single(r).Automatic);Assert.Equal("Completed",r[0].State);});}
    [Fact]public async Task Another_staff_member_cannot_run_the_preview()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates();var p=await Preview(Service(Guid.NewGuid()));await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(Guid.NewGuid()).RunAsync(new(){RunId=p.RunId!.Value}));await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);}
    [Fact]public async Task Expired_previews_never_write()
    {using var t=Tenant.Change(Guid.NewGuid());var actor=Guid.NewGuid();var id=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>{var run=new ClearanceResetRun(id,Tenant.Id!.Value);run.MakePreview([Row()],actor,Now.AddMinutes(-16));await Runs.InsertAsync(run,true);});await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(actor).RunAsync(new(){RunId=id}));await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);}
    [Fact]public async Task Manual_run_cannot_overlap_the_automatic_claim()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates();var service=Service(Guid.NewGuid());var p=await Preview(service);await Configure();await WithUnitOfWorkAsync(async()=>{var c=Assert.Single(await Configs.GetListAsync());c.SetRun(Guid.NewGuid());await Configs.UpdateAsync(c,true);});await Assert.ThrowsAsync<UserFriendlyException>(()=>service.RunAsync(new(){RunId=p.RunId!.Value}));await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);}
    [Fact]public async Task Large_manual_batches_require_narrowing_and_invalid_UID_does_not_broaden_scope()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates(101);var service=Service(Guid.NewGuid());var all=await Preview(service);Assert.Null(all.RunId);Assert.Equal(101,all.Rows.Count);var one=await Preview(service,1);Assert.NotNull(one.RunId);Assert.Equal(1,Assert.Single(one.Rows).StudentUid);await Assert.ThrowsAsync<UserFriendlyException>(()=>Preview(service,0));}
    [Fact]public async Task Manual_reconciliation_does_not_require_enabling_automation()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates();await Configure(false);var failure=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>{var run=new ClearanceResetRun(failure,Tenant.Id!.Value);run.Finish("Needs review","Unknown");await Runs.InsertAsync(run,true);});var service=Service(Guid.NewGuid());var p=await Preview(service);await Assert.ThrowsAsync<UserFriendlyException>(()=>service.RunAsync(new(){RunId=p.RunId!.Value}));await service.RunAsync(new(){RunId=p.RunId!.Value,ReviewedRunIds=[failure]});await Check((c,r)=>{Assert.False(c.Schedule().Enabled);Assert.True(r.Single(x=>x.Id==failure).Reviewed);});}
    [Fact]public async Task Preview_remains_read_only_when_execution_is_disabled()
    {using var t=Tenant.Change(Guid.NewGuid());Candidates();_elements.When(x=>x.EnsureRunEnabled()).Do(_=>throw new InvalidOperationException("Disabled"));var p=await Preview(Service(Guid.NewGuid()));Assert.Null(p.RunId);Assert.Single(p.Rows);await _elements.DidNotReceiveWithAnyArgs().ResetIfEligibleAsync(default!,default!,default);}

}
