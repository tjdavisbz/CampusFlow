using System;
using CampusFlow.ClearanceResets;
using Xunit;
namespace CampusFlow.ClearanceResets.Tests;
public class ClearanceResetScheduleTests
{
    private static readonly DateTime Now=new(2026,11,7,20,0,0,DateTimeKind.Utc);
    [Fact]public void Previously_enabled_policy_requires_explicit_reenable_for_the_expanded_fields()
    {
        var old=new ClearanceResetSchedule{Enabled=true,PolicyVersion=2,NextRunAtUtc=Now};
        Assert.False(old.IsDue(Now));old.Configure(true,Guid.NewGuid(),Now);
        Assert.Equal(ClearanceResetSchedule.CurrentPolicyVersion,old.PolicyVersion);
        Assert.Equal(Now.AddMinutes(15),old.NextRunAtUtc);
    }
    [Fact]public void Existing_and_new_configuration_defaults_off()
    {var c=new ClearanceResetConfiguration(Guid.NewGuid(),Guid.NewGuid());Assert.False(c.Schedule().Enabled);Assert.False(c.Schedule().IsDue(Now));}
    [Fact]public void Enabling_waits_fifteen_minutes_and_persists_audit_actor()
    {var c=new ClearanceResetConfiguration(Guid.NewGuid(),Guid.NewGuid());var actor=Guid.NewGuid();var s=c.Schedule();s.Configure(true,actor,Now);c.Save(s);var loaded=c.Schedule();Assert.False(loaded.IsDue(Now.AddMinutes(14)));Assert.True(loaded.IsDue(Now.AddMinutes(15)));Assert.Equal(actor,loaded.ChangedBy);}
    [Fact]public void Disabling_prevents_new_work_and_finishing_does_not_reenable()
    {var s=new ClearanceResetSchedule();var actor=Guid.NewGuid();s.Configure(true,actor,Now);s.Started(Now.AddMinutes(15));s.Configure(false,actor,Now.AddMinutes(16));s.Finished(Now.AddMinutes(17),"Stopped",false);Assert.False(s.Enabled);Assert.Null(s.NextRunAtUtc);}
    [Fact]public void Slow_cycles_do_not_create_catchup_bursts()
    {var s=new ClearanceResetSchedule();s.Configure(true,Guid.NewGuid(),Now);s.Started(Now.AddMinutes(15));s.Finished(Now.AddMinutes(35),"Completed",false);Assert.Equal(Now.AddMinutes(50),s.NextRunAtUtc);}
    [Fact]public void Unknown_outcome_pauses_and_review_does_not_erase_original_history()
    {var s=new ClearanceResetSchedule();s.Configure(true,Guid.NewGuid(),Now);s.Finished(Now,"Unconfirmed",true);Assert.False(s.Enabled);Assert.Null(s.NextRunAtUtc);var run=new ClearanceResetRun(Guid.NewGuid(),Guid.NewGuid());run.Finish("Needs review","Unconfirmed");run.Review(Guid.NewGuid(),Now);Assert.True(run.Reviewed);Assert.Equal("Needs review",run.State);}
    [Fact]public void A_running_claim_cannot_be_acknowledged_as_a_finished_failure()
    {var run=new ClearanceResetRun(Guid.NewGuid(),Guid.NewGuid());Assert.Throws<InvalidOperationException>(()=>run.Review(Guid.NewGuid(),Now));}
}
