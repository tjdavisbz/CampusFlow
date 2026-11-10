using System;
using System.Linq;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillFinishExperienceTests
{
    [Theory]
    [InlineData(BillCompletionStatus.Pending,false)]
    [InlineData(BillCompletionStatus.Running,false)]
    [InlineData(BillCompletionStatus.Failed,false)]
    [InlineData(BillCompletionStatus.Uncertain,false)]
    [InlineData(BillCompletionStatus.Completed,true)]
    public void Registration_complete_requires_verified_clearance(BillCompletionStatus status,bool complete)
    {
        var approval=new BillApproval(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"10","100","269","B27S","Spring 2027");
        approval.Accept(Guid.NewGuid(),1,"<p>Accepted terms.</p>",DateTime.UtcNow,null,null);
        var progress=new BillApprovalCompletionProgress();progress.Step(BillCompletionStep.Cleared).Status=status;progress.Save(approval);
        Assert.Equal(complete,BillApprovalStudentCompletion.RegistrationComplete(approval));
        var journey=new IndexModel.RegistrationJourneyViewModel{Term=new(CampusFlow.StudentInformationSystems.StudentInformationSystemProvider.ThesisElements,"269","B27S","Spring 2027",new(2027,1,1),new(2027,5,1)),BillApproved=true,RegistrationComplete=complete};journey.BuildSteps();
        Assert.Equal(complete?BillApprovalStudentCompletion.CompleteMessage:BillApprovalStudentCompletion.PendingMessage,journey.Steps.Single(x=>x.Title=="Bill approval").Description);
    }
    [Fact]public void Refund_rows_keep_percentage_and_date_together_and_preserve_following_terms()
    {
        var blocks=AgreementPdfContent.Read("<h2>Refunds</h2><table><tbody><tr><td><p>100%</p></td><td><p>January 11–17, 2027</p></td></tr><tr><td>75%</td><td>January 18–24, 2027</td></tr></tbody></table><p>Terms &amp; conditions still apply.</p>");
        Assert.True(blocks[0].IsHeading);Assert.Equal("100%",blocks[1].Rows![0][0]);Assert.Equal("January 11–17, 2027",blocks[1].Rows![0][1]);Assert.Equal("75%",blocks[1].Rows![1][0]);Assert.Equal("Terms & conditions still apply.",blocks[2].Text);
    }
    [Fact]public void Ordinary_short_sentences_are_not_mistaken_for_headings_or_lost()
    {
        var blocks=AgreementPdfContent.Read("<div><p>I agree<br/>to these terms.</p><ul><li>Keep my copy.</li></ul></div>");
        Assert.Equal(new[]{"I agree to these terms.","- Keep my copy."},blocks.Select(x=>x.Text));Assert.All(blocks,x=>Assert.False(x.IsHeading));
    }
}
