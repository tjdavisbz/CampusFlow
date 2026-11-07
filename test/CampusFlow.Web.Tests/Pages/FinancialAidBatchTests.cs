using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using CampusFlow.StudentInformationSystems;using CampusFlow.Web.Pages;using NSubstitute;using Xunit;
namespace CampusFlow.Pages;
public class FinancialAidBatchTests
{
    internal static StudentFinancialAidAward Award(string id="1",string term="269")=>new(StudentInformationSystemProvider.ThesisElements,id,term,term=="269"?"B27C":"B26Q","Term",DateTime.Today,"Grant","Guarantee","Award",100,false,null,null,false);
    static (IStudentInformationSystemFinancialAidLookup lookup,IStudentInformationSystemFinancialAidDecisionService service) Setup(params StudentFinancialAidAward[] awards)
    {
        var lookup=Substitute.For<IStudentInformationSystemFinancialAidLookup>();lookup.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(awards);
        var service=Substitute.For<IStudentInformationSystemFinancialAidDecisionService>();service.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(awards.ToDictionary(x=>x.ExternalAwardId,x=>x.StudentAccepted));return(lookup,service);
    }
    [Fact] public async Task Only_the_reviewed_semester_is_accepted()
    {
        var(l,s)=Setup(Award(),Award("2"),Award("3","268"));var result=await FinancialAidBatch.AcceptAsync("10","269","1,2",l,s);
        Assert.Equal(2,result.Confirmed);Assert.False(result.Unconfirmed);await s.DidNotReceive().SubmitDecisionAsync("10","3",Arg.Any<StudentFinancialAidDecision>(),Arg.Any<CancellationToken>(),"269");
        Assert.Equal(2,s.ReceivedCalls().Count(x=>x.GetMethodInfo().Name=="SubmitDecisionAsync"));
    }
    [Theory] [InlineData("1,3")] [InlineData("1,1")] [InlineData("1,999")] [InlineData("1")] [InlineData("")] [InlineData("abc")]
    public async Task Forged_stale_cross_term_or_incomplete_confirmations_make_no_writes(string ids)
    {var(l,s)=Setup(Award(),Award("2"),Award("3","268"));await Assert.ThrowsAsync<InvalidOperationException>(()=>FinancialAidBatch.AcceptAsync("10","269",ids,l,s));Assert.DoesNotContain(s.ReceivedCalls(),x=>x.GetMethodInfo().Name=="SubmitDecisionAsync");}
    [Fact] public async Task Fresh_decision_prevents_reaccepting_a_declined_award_from_a_stale_replica()
    {var(l,s)=Setup(Award());s.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(new Dictionary<string,int?>{{"1",0}});await Assert.ThrowsAsync<InvalidOperationException>(()=>FinancialAidBatch.AcceptAsync("10","269","1",l,s));Assert.DoesNotContain(s.ReceivedCalls(),x=>x.GetMethodInfo().Name=="SubmitDecisionAsync");}
    [Fact] public async Task Missing_live_decision_fails_before_any_write()
    {var(l,s)=Setup(Award());s.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(new Dictionary<string,int?>());await Assert.ThrowsAsync<InvalidOperationException>(()=>FinancialAidBatch.AcceptAsync("10","269","1",l,s));Assert.DoesNotContain(s.ReceivedCalls(),x=>x.GetMethodInfo().Name=="SubmitDecisionAsync");}
    [Fact] public async Task An_unconfirmed_write_stops_the_batch_and_is_not_retried()
    {var(l,s)=Setup(Award(),Award("2"),Award("3"));s.SubmitDecisionAsync("10","2",StudentFinancialAidDecision.Accept,Arg.Any<CancellationToken>(),"269").Returns(Task.FromException(new InvalidOperationException("Unconfirmed")));var result=await FinancialAidBatch.AcceptAsync("10","269","1,2,3",l,s);Assert.Equal(1,result.Confirmed);Assert.True(result.Unconfirmed);await s.Received(1).SubmitDecisionAsync("10","2",StudentFinancialAidDecision.Accept,Arg.Any<CancellationToken>(),"269");await s.DidNotReceive().SubmitDecisionAsync("10","3",Arg.Any<StudentFinancialAidDecision>(),Arg.Any<CancellationToken>(),"269");}
    [Fact] public async Task Locked_cancelled_billed_and_nonpositive_awards_are_not_implicitly_added()
    {var(l,s)=Setup(Award(),Award("2") with{IsLocked=true},Award("3") with{IsSentToBilling=true},Award("4") with{AwardStatus="Cancelled"},Award("5") with{Amount=0});var r=await FinancialAidBatch.AcceptAsync("10","269","1",l,s);Assert.Equal(1,r.Confirmed);Assert.Single(s.ReceivedCalls().Where(x=>x.GetMethodInfo().Name=="SubmitDecisionAsync"));}
}
