using System;
using CampusFlow.BillApprovals;
using CampusFlow.ClearanceResets;
using Xunit;
namespace CampusFlow.ClearanceResets.Tests;
public class ClearanceApprovalRulesTests
{
    [Theory][InlineData(BillApprovalStatus.Draft,false)][InlineData(BillApprovalStatus.AwaitingPayment,false)][InlineData(BillApprovalStatus.Approved,true)][InlineData(BillApprovalStatus.DocumentPending,true)][InlineData(BillApprovalStatus.Completed,true)][InlineData(BillApprovalStatus.Failed,false)]
    public void Approved_work_remains_protected_while_documents_or_emails_finish(BillApprovalStatus status,bool expected)
    {Assert.Equal(expected,BillApprovalAcceptanceRules.IsApproved(DateTime.UtcNow,status));Assert.False(BillApprovalAcceptanceRules.IsApproved(null,status));}
    [Fact]public void Old_enabled_schedules_cannot_silently_adopt_the_broader_rule()
    {
        var now=DateTime.UtcNow;var old=new ClearanceResetSchedule{Enabled=true,NextRunAtUtc=now.AddHours(-1)};
        Assert.False(old.IsDue(now));old.Configure(true,Guid.NewGuid(),now);Assert.False(old.IsDue(now.AddMinutes(14)));Assert.True(old.IsDue(now.AddMinutes(15)));Assert.Equal(ClearanceResetSchedule.CurrentPolicyVersion,old.PolicyVersion);
    }
}
