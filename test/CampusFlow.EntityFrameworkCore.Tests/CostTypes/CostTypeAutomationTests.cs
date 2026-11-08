using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.CostTypes;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class CostTypeAutomationTests : CampusFlowEntityFrameworkCoreTestBase
{
    private readonly ICostTypeElements _elements=Substitute.For<ICostTypeElements>();
    private readonly IClock _clock=Substitute.For<IClock>();
    private static readonly DateTime Now=new(2026,11,7,12,0,0,DateTimeKind.Utc);
    private IRepository<CostTypeConfiguration,Guid> Configs=>GetRequiredService<IRepository<CostTypeConfiguration,Guid>>();
    private IRepository<CostTypeRun,Guid> Runs=>GetRequiredService<IRepository<CostTypeRun,Guid>>();
    private IUnitOfWorkManager Units=>GetRequiredService<IUnitOfWorkManager>();
    private ICurrentTenant Tenant=>GetRequiredService<ICurrentTenant>();
    private CostTypeAutomationRunner Runner()
    {
        _clock.Now.Returns(Now);var executor=new CostTypeRunExecutor(Configs,Runs,_elements,Units,_clock);
        return new(Configs,Runs,_elements,Units,Tenant,GetRequiredService<IGuidGenerator>(),_clock,executor);
    }
    private async Task<Guid> Configure(bool enabled=true,DateTime? enabledAt=null)
    {
        var id=Guid.NewGuid();await WithUnitOfWorkAsync(async()=>
        {
            var rules=JsonSerializer.Serialize(new[]{new CostTypeRule(1,null,"Distance Education","ANY",null,"DE",4)});
            var c=new CostTypeConfiguration(id,Tenant.Id!.Value,rules,"Reviewed rules");
            var schedule=CostTypeAutomationState.Read(c);schedule.Configure(enabled,Guid.NewGuid(),enabledAt??Now.AddMinutes(-15));schedule.Save(c);
            await Configs.InsertAsync(c,true);
        });return id;
    }
    private static CostTypePreviewRow Row(int student=10,string action="Would change")=>new(student,"test","Test",student+1000,269,"Spring 2027","MAIN","Distance Education",null,26,"AC",1,4,"DE",action);
    private void Rows(params CostTypePreviewRow[] rows)
    {
        _elements.PreviewAsync(Arg.Any<List<CostTypeRule>>(),null,Arg.Any<CancellationToken>()).Returns(rows.ToList());
        _elements.FingerprintAsync(Arg.Any<CostTypePreviewRow>(),Arg.Any<CancellationToken>()).Returns("fingerprint");
    }
    private async Task AssertState(Action<CostTypeConfiguration,List<CostTypeRun>> check)=>await WithUnitOfWorkAsync(async()=>check(Assert.Single(await Configs.GetListAsync()),await Runs.GetListAsync()));
    [Fact] public async Task Disabled_or_not_yet_due_schedules_make_no_Elements_calls()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure(false);Assert.False(await Runner().RunIfDueAsync());
        await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);
        await AssertState((c,r)=>Assert.Empty(r));
    }
    [Fact] public async Task A_new_schedule_waits_fifteen_minutes()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure(true,Now);Assert.False(await Runner().RunIfDueAsync());
        await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);
    }
    [Fact] public async Task A_due_cycle_uses_fresh_checks_records_automatic_history_and_does_not_run_again_immediately()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row());var runner=Runner();Assert.True(await runner.RunIfDueAsync());Assert.False(await runner.RunIfDueAsync());
        await _elements.Received(1).ApplyAsync(Arg.Is<CostTypeCandidate>(x=>x.Row.StudentUid==10&&x.Row.TermId==269),Arg.Any<CancellationToken>());
        await AssertState((c,r)=>{var run=Assert.Single(r);Assert.True(run.IsAutomatic);Assert.Equal("Completed",run.State);Assert.NotEqual("[]",run.RulesSnapshotJson);Assert.Null(c.RunningRunId);Assert.Equal(Now.AddMinutes(15),CostTypeAutomationState.Read(c).NextRunAtUtc);});
    }
    [Fact] public async Task No_change_cycles_are_compact_and_never_write_invalid_or_duplicate_records()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row(10,"Already correct"),Row(11,"Unmapped"),Row(12,"Invalid lookup"),Row(13,"Duplicate record"));
        Assert.True(await Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().ApplyAsync(default!,default);
        await _elements.DidNotReceiveWithAnyArgs().FingerprintAsync(default!,default);
        await AssertState((c,r)=>{var run=Assert.Single(r);Assert.Equal("[]",run.RulesSnapshotJson);Assert.Contains("No changes needed",run.Summary);});
    }
    [Fact] public async Task A_cycle_applies_at_most_one_hundred_changes()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Enumerable.Range(1,101).Select(x=>Row(x)).ToArray());
        await Runner().RunIfDueAsync();await _elements.Received(100).ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>());
        await AssertState((c,r)=>{var run=Assert.Single(r);Assert.Equal(100,JsonSerializer.Deserialize<List<CostTypeResultDto>>(run.ResultsJson)!.Count);Assert.Contains("1 additional",run.Summary);});
    }
    [Fact] public async Task An_unconfirmed_apply_pauses_automation_and_is_never_automatically_retried()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row(),Row(11));
        _elements.ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Unconfirmed write")));
        var runner=Runner();await runner.RunIfDueAsync();Assert.False(await runner.RunIfDueAsync());
        await _elements.Received(1).ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>());
        await AssertState((c,r)=>{Assert.Equal("Needs review",Assert.Single(r).State);Assert.False(CostTypeAutomationState.Read(c).Enabled);Assert.Null(CostTypeAutomationState.Read(c).NextRunAtUtc);});
    }
    [Fact] public async Task Turning_off_during_a_run_stops_before_the_next_update()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row(),Row(11));
        _elements.ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>()).Returns(async _=>
        {
            await WithUnitOfWorkAsync(async()=>{var c=Assert.Single(await Configs.GetListAsync());var state=CostTypeAutomationState.Read(c);state.Configure(false,Guid.NewGuid(),Now);state.Save(c);await Configs.UpdateAsync(c,true);});
        });
        await Runner().RunIfDueAsync();await _elements.Received(1).ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>());
        await AssertState((c,r)=>{Assert.Equal("Stopped",Assert.Single(r).State);Assert.False(CostTypeAutomationState.Read(c).Enabled);Assert.Null(c.RunningRunId);});
    }
    [Fact] public async Task Environment_guard_runs_before_claim_or_external_access()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();_elements.When(x=>x.EnsureRunEnabled()).Do(_=>throw new InvalidOperationException("Wrong environment"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Runner().RunIfDueAsync());
        await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);await AssertState((c,r)=>Assert.Empty(r));
    }
    [Fact] public async Task An_existing_or_interrupted_run_prevents_overlap_even_after_restart()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();var id=Guid.NewGuid();
        await WithUnitOfWorkAsync(async()=>{var c=Assert.Single(await Configs.GetListAsync());var run=new CostTypeRun(id,Tenant.Id!.Value,c.RulesRevision,c.RulesJson,"[]",null);run.Start();await Runs.InsertAsync(run,true);c.SetRun(id);await Configs.UpdateAsync(c,true);});
        Assert.False(await Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);await AssertState((c,r)=>Assert.Single(r));
    }
    [Fact] public async Task A_preparation_failure_can_check_again_later_without_any_apply_attempt()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row());
        _elements.FingerprintAsync(Arg.Any<CostTypePreviewRow>(),Arg.Any<CancellationToken>()).Returns(Task.FromException<string>(new InvalidOperationException("Replica changed")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().ApplyAsync(default!,default);
        await AssertState((c,r)=>{Assert.Equal("Failed before changes",Assert.Single(r).State);Assert.True(CostTypeAutomationState.Read(c).Enabled);Assert.Equal(Now.AddMinutes(15),CostTypeAutomationState.Read(c).NextRunAtUtc);});
    }
    [Fact] public async Task Existing_unconfirmed_manual_history_pauses_an_automatic_cycle()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();await WithUnitOfWorkAsync(async()=>{var run=new CostTypeRun(Guid.NewGuid(),Tenant.Id!.Value,"r","[]","[]",null);run.Finish("Needs review");await Runs.InsertAsync(run,true);});
        Assert.False(await Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().ApplyAsync(default!,default);
        await AssertState((c,r)=>{Assert.Single(r);Assert.False(CostTypeAutomationState.Read(c).Enabled);});
    }
    [Fact] public async Task No_host_tenant_can_start_an_automatic_cycle()
    {using var tenant=Tenant.Change(null);Assert.False(await Runner().RunIfDueAsync());await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);}
    private CostTypeAppService Service(Guid? actor)
    {
        var lazy=Substitute.For<IAbpLazyServiceProvider>();var user=Substitute.For<ICurrentUser>();user.Id.Returns(actor);
        _clock.Now.Returns(Now);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(Tenant);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<IClock>().Returns(_clock);
        return new(Configs,Runs,_elements,Units){LazyServiceProvider=lazy};
    }
    private async Task<string> Revision()
    {string value="";await AssertState((c,r)=>value=c.RulesRevision);return value;}
    [Fact] public async Task Page_toggle_persists_without_starting_a_run_or_invalidating_saved_rules()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure(false);var revision=await Revision();var actor=Guid.NewGuid();
        var result=await Service(actor).SetAutomaticRunsAsync(new(){Enabled=true,Revision=revision});
        Assert.True(result.Enabled);Assert.Equal(Now.AddMinutes(15),result.NextRunAtUtc);Assert.Equal(revision,await Revision());
        await AssertState((c,r)=>{Assert.Empty(r);Assert.Equal(actor,CostTypeAutomationState.Read(c).ChangedBy);});
        await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default,default);
    }
    [Fact] public async Task Page_can_turn_off_during_a_run_with_stale_rule_revision_and_Elements_unavailable()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();var revision=await Revision();var runId=Guid.NewGuid();
        await WithUnitOfWorkAsync(async()=>{var c=Assert.Single(await Configs.GetListAsync());c.SetRun(runId);await Configs.UpdateAsync(c,true);});
        _elements.When(x=>x.EnsureRunEnabled()).Do(_=>throw new InvalidOperationException("Unavailable"));
        var result=await Service(Guid.NewGuid()).SetAutomaticRunsAsync(new(){Enabled=false,Revision="old"});
        Assert.False(result.Enabled);Assert.Null(result.NextRunAtUtc);Assert.Equal(runId,result.RunningRunId);Assert.Equal(revision,await Revision());
        _elements.DidNotReceive().EnsureRunEnabled();
    }
    [Fact] public async Task Enabling_requires_current_saved_rules_and_a_signed_in_actor()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure(false);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(null).SetAutomaticRunsAsync(new(){Enabled=true,Revision=""}));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(Guid.NewGuid()).SetAutomaticRunsAsync(new(){Enabled=true,Revision="stale"}));
        await AssertState((c,r)=>Assert.False(CostTypeAutomationState.Read(c).Enabled));
    }
    [Fact] public async Task Only_explicitly_reviewed_unconfirmed_history_can_be_reenabled_and_original_outcome_is_retained()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure(false);var runId=Guid.NewGuid();
        await WithUnitOfWorkAsync(async()=>{var run=new CostTypeRun(runId,Tenant.Id!.Value,"r","[]","[]",null);run.Finish("Needs review");await Runs.InsertAsync(run,true);});
        var revision=await Revision();var actor=Guid.NewGuid();var service=Service(actor);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>service.SetAutomaticRunsAsync(new(){Enabled=true,Revision=revision}));
        await Assert.ThrowsAsync<UserFriendlyException>(()=>service.SetAutomaticRunsAsync(new(){Enabled=true,Revision=revision,ReviewedRunIds=[Guid.NewGuid()]}));
        var result=await service.SetAutomaticRunsAsync(new(){Enabled=true,Revision=revision,ReviewedRunIds=[runId]});
        Assert.True(result.Enabled);Assert.Empty(result.ReviewRuns);
        await AssertState((c,r)=>{var run=Assert.Single(r);Assert.Equal("Needs review",run.State);Assert.True(run.IsReviewed);});
    }
    [Fact] public async Task A_second_worker_cannot_overlap_a_claimed_cycle()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());await Configure();Rows(Row());
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var previews=0;
        _elements.PreviewAsync(Arg.Any<List<CostTypeRule>>(),null,Arg.Any<CancellationToken>()).Returns(async _=>
        {if(Interlocked.Increment(ref previews)==1){entered.SetResult();await release.Task;}return new List<CostTypePreviewRow>{Row()};});
        var first=Runner().RunIfDueAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try{Assert.False(await Runner().RunIfDueAsync());Assert.Equal(1,previews);}
        finally{release.SetResult();}
        Assert.True(await first);await _elements.Received(1).ApplyAsync(Arg.Any<CostTypeCandidate>(),Arg.Any<CancellationToken>());
        await AssertState((c,r)=>Assert.Single(r));
    }

    [Fact] public async Task Competing_claims_cannot_overwrite_the_database_run_owner()
    {
        using var tenant=Tenant.Change(Guid.NewGuid());var id=await Configure();var winner=Guid.NewGuid();
        using(var staleUnit=Units.Begin(requiresNew:true,isTransactional:false))
        {
            var stale=await Configs.GetAsync(id);
            using(var claim=Units.Begin(requiresNew:true,isTransactional:false))
            {
                var current=await Configs.GetAsync(id);current.SetRun(winner);
                await Configs.UpdateAsync(current,true);await claim.CompleteAsync();
            }
            stale.SetRun(Guid.NewGuid());
            await Assert.ThrowsAsync<AbpDbConcurrencyException>(()=>Configs.UpdateAsync(stale,true));
        }
        await AssertState((c,r)=>Assert.Equal(winner,c.RunningRunId));
    }

}
