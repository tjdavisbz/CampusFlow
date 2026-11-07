using System;
using CampusFlow.StudentInformationSystems;
using Xunit;
namespace CampusFlow.StudentInformationSystems;
public class FinancialAidRulesTests
{
    [Theory]
    [InlineData("TEG","Guarantee",false,null,true)]
    [InlineData("teg "," guarantee ",false,1,true)]
    [InlineData("TEG","Estimated",false,1,false)]
    [InlineData("FWS","Guarantee",false,1,false)]
    [InlineData("twws","Guarantee",false,1,false)]
    [InlineData(" TX MENTOR ","Guarantee",false,1,false)]
    [InlineData("UNSUB ADD","Guarantee",true,null,false)]
    [InlineData("UNSUB ADD","Guarantee",true,0,false)]
    [InlineData("UNSUB ADD","Guarantee",true,1,true)]
    [InlineData("Private loan with arbitrary name","Guarantee",true,null,false)]
    [InlineData("Scholarship","Awarded",false,null,true)]
    [InlineData("Scholarship","Awarded",false,0,false)]
    [InlineData("Unknown type","Awarded",null,null,false)]
    [InlineData("Unknown type","Awarded",null,1,true)]
    [InlineData("Scholarship","Cancelled",false,1,false)]
    public void Bill_rules_use_actual_loan_metadata_and_exact_award_policy(string type,string status,bool? loan,int? accepted,bool expected)
    { var award=Award() with {AwardType=type,AwardStatus=status,IsLoan=loan,StudentAccepted=accepted};Assert.Equal(expected,FinancialAidRules.IncludeInBillApproval(award)); }
    [Theory] [InlineData(0)] [InlineData(-10)]
    public void Nonpositive_awards_neither_count_nor_offer_decisions(decimal amount)
    {var a=Award() with {Amount=amount};Assert.False(FinancialAidRules.IncludeInBillApproval(a));Assert.False(FinancialAidRules.CanDecide(a));}
    [Fact] public void Billed_aid_is_not_double_counted_or_accepted_again()
    {var a=Award() with {IsSentToBilling=true};Assert.False(FinancialAidRules.IncludeInBillApproval(a));Assert.False(FinancialAidRules.CanDecide(a));}
    [Fact] public void Locked_awards_and_recorded_decisions_are_not_bulk_candidates()
    {Assert.False(FinancialAidRules.CanDecide(Award() with {IsLocked=true}));Assert.False(FinancialAidRules.CanDecide(Award() with {StudentAccepted=0}));Assert.False(FinancialAidRules.CanDecide(Award() with {StudentAccepted=1}));Assert.True(FinancialAidRules.CanDecide(Award()));}
    [Fact] public void Other_providers_keep_their_existing_pending_aid_behavior()
    {var provider=Array.Find(Enum.GetValues<StudentInformationSystemProvider>(),x=>x!=StudentInformationSystemProvider.ThesisElements);Assert.True(FinancialAidRules.IncludeInBillApproval(Award() with {Provider=provider,IsLoan=null}));}
    static StudentFinancialAidAward Award()=>new(StudentInformationSystemProvider.ThesisElements,"1","269","B27C","Spring 2027",DateTime.Today,"Grant","Guarantee","Grant",100,false,null,null,false);
}
