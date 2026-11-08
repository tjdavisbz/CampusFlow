using System;
using CampusFlow.CostTypes;
using Xunit;

namespace CampusFlow.CostTypes;
public class CostTypeAutomationStateTests
{
    private static readonly DateTime Now=new(2026,11,7,12,0,0,DateTimeKind.Utc);
    [Fact] public void Existing_saved_rules_are_off_until_explicitly_enabled()
    {
        var c=new CostTypeConfiguration(Guid.NewGuid(),Guid.NewGuid(),"[]","saved");var state=CostTypeAutomationState.Read(c);
        Assert.False(state.Enabled);Assert.False(state.IsDue(Now));Assert.Null(state.NextRunAtUtc);
    }
    [Fact] public void Enable_is_due_after_fifteen_minutes_and_survives_serialization()
    {
        var c=new CostTypeConfiguration(Guid.NewGuid(),Guid.NewGuid(),"[]","saved");var state=CostTypeAutomationState.Read(c);
        state.Configure(true,Guid.NewGuid(),Now);state.Save(c);var loaded=CostTypeAutomationState.Read(c);
        Assert.False(loaded.IsDue(Now.AddMinutes(15).AddTicks(-1)));Assert.True(loaded.IsDue(Now.AddMinutes(15)));
        Assert.Equal(Now.AddMinutes(15),loaded.NextRunAtUtc);
    }
    [Fact] public void Schedule_metadata_does_not_change_the_saved_rule_revision()
    {
        var c=new CostTypeConfiguration(Guid.NewGuid(),Guid.NewGuid(),"[]","saved");var revision=c.RulesRevision;
        var state=CostTypeAutomationState.Read(c);state.Configure(true,Guid.NewGuid(),Now);state.Save(c);c.ConcurrencyStamp="changed-by-schedule";
        Assert.Equal(revision,c.RulesRevision);c.Edit("[]","new rules");Assert.NotEqual(revision,c.RulesRevision);
    }
    [Fact] public void Long_runs_do_not_catch_up_missed_cycles_and_disabled_runs_stay_off()
    {
        var state=new CostTypeAutomationState();state.Configure(true,Guid.NewGuid(),Now);state.Started(Guid.NewGuid(),Now.AddMinutes(15));
        state.Finished(Now.AddHours(1),"done",false);Assert.Equal(Now.AddMinutes(75),state.NextRunAtUtc);
        state.Configure(false,Guid.NewGuid(),Now.AddMinutes(61));state.Finished(Now.AddMinutes(62),"stopped",false);
        Assert.False(state.Enabled);Assert.Null(state.NextRunAtUtc);
    }
    [Fact] public void Unconfirmed_writes_pause_and_review_does_not_erase_the_failure()
    {
        var state=new CostTypeAutomationState();state.Configure(true,Guid.NewGuid(),Now);state.Started(Guid.NewGuid(),Now);
        state.Finished(Now.AddMinutes(1),"unconfirmed",true);Assert.False(state.Enabled);Assert.Null(state.NextRunAtUtc);
        var run=new CostTypeRun(Guid.NewGuid(),Guid.NewGuid(),"revision","[]","[]",null);run.Finish("Needs review");run.Review(Guid.NewGuid(),Now);
        Assert.True(run.IsReviewed);Assert.Equal("Needs review",run.State);
    }
    [Fact] public void An_inflight_run_cannot_be_confirmed_as_reviewed()
    {
        var run=new CostTypeRun(Guid.NewGuid(),Guid.NewGuid(),"revision","[]","[]",null);run.Start();
        Assert.Throws<InvalidOperationException>(()=>run.Review(Guid.NewGuid(),Now));Assert.False(run.IsReviewed);
    }
}
