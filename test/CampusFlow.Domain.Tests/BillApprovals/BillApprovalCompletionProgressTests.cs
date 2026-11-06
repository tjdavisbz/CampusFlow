using System;
using CampusFlow.BillApprovals;
using Xunit;
namespace CampusFlow.Domain.Tests.BillApprovals;
public class BillApprovalCompletionProgressTests
{
    private static readonly DateTime Now=new(2026,11,5,23,0,0,DateTimeKind.Utc);
    [Fact] public void Active_runs_cannot_be_started_twice()
    {var p=new BillApprovalCompletionProgress();Assert.True(p.Begin(Guid.NewGuid(),Now));Assert.False(p.Begin(Guid.NewGuid(),Now.AddMinutes(1)));Assert.Throws<InvalidOperationException>(()=>p.ResetForRetry(Now.AddMinutes(1),true));}
    [Fact] public void Uncertain_email_is_blocked_until_confirmed_unsent()
    {
        var p=new BillApprovalCompletionProgress();p.Finish(BillCompletionStep.Pdf,BillCompletionStatus.Completed);p.Finish(BillCompletionStep.Cleared,BillCompletionStatus.Completed);
        p.Finish(BillCompletionStep.StudentEmail,BillCompletionStatus.Uncertain);
        p.ResetForRetry(Now,false);Assert.False(p.Start(BillCompletionStep.StudentEmail,Now));
        p.ResetForRetry(Now,true);Assert.True(p.Start(BillCompletionStep.StudentEmail,Now));
    }
    [Fact] public void Stale_email_claim_is_uncertain_while_idempotent_work_can_be_retried()
    {
        var p=new BillApprovalCompletionProgress();p.Begin(Guid.NewGuid(),Now);p.Start(BillCompletionStep.Pdf,Now);
        p.Finish(BillCompletionStep.Cleared,BillCompletionStatus.Completed);p.Finish(BillCompletionStep.Pdf,BillCompletionStatus.Completed);p.Start(BillCompletionStep.StudentEmail,Now);
        Assert.True(p.Begin(Guid.NewGuid(),Now.AddMinutes(31)));Assert.Equal(BillCompletionStatus.Uncertain,p.Step(BillCompletionStep.StudentEmail).Status);
        Assert.Equal(BillCompletionStatus.Completed,p.Step(BillCompletionStep.Pdf).Status);
    }
    [Fact] public void Completed_steps_and_native_approval_are_preserved_on_retry_and_round_trip()
    {
        var bill=new BillApproval(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"10","100","269","B27S","Spring 2027");bill.Accept(Guid.NewGuid(),1,"Agreement",Now,null,null);
        var p=new BillApprovalCompletionProgress();foreach(var step in Enum.GetValues<BillCompletionStep>())p.Finish(step,BillCompletionStatus.Completed);
        p.Save(bill);var read=BillApprovalCompletionProgress.Read(bill);read.ResetForRetry(Now,true);
        Assert.True(read.IsComplete);Assert.False(read.Start(BillCompletionStep.StudentEmail,Now));Assert.Equal(BillApprovalStatus.Approved,bill.Status);
        Assert.True(CampusFlow.Bibliu.BibliuRules.IsApproved(bill));
    }
    [Theory][InlineData(BillCompletionStep.StudentEmail)][InlineData(BillCompletionStep.BookstoreEmail)]
    public void Notification_waits_for_pdf_and_cleared_status(BillCompletionStep step)
    {var p=new BillApprovalCompletionProgress();Assert.False(p.Start(step,Now));p.Finish(BillCompletionStep.Pdf,BillCompletionStatus.Completed);Assert.False(p.Start(step,Now));p.Finish(BillCompletionStep.Cleared,BillCompletionStatus.Completed);Assert.True(p.Start(step,Now));}
}
