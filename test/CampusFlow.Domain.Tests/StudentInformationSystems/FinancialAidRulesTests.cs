using System;
using CampusFlow.StudentInformationSystems;
using Xunit;
namespace CampusFlow.StudentInformationSystems;
public class FinancialAidRulesTests
{
    [Fact]
    public void Bill_amount_uses_stored_net_without_changing_gross_decision_amount()
    {
        var award = Award() with { Amount = 1000m, DistributionAmount = 989.43m };
        Assert.Equal(989.43m, FinancialAidRules.BillApprovalAmount(award));
        Assert.Equal(1000m, award.Amount);
        Assert.True(FinancialAidRules.CanDecide(award));
    }

    [Fact]
    public void Missing_or_invalid_distribution_amount_does_not_fall_back_to_gross()
    {
        Assert.Throws<InvalidOperationException>(() => FinancialAidRules.BillApprovalAmount(Award()));
        Assert.Throws<InvalidOperationException>(() => FinancialAidRules.BillApprovalAmount(Award() with { DistributionAmount = -1 }));
        Assert.Equal(0m, FinancialAidRules.BillApprovalAmount(Award() with { DistributionAmount = 0 }));
    }

    [Fact]
    public void Other_providers_bill_their_existing_award_amount()
    {
        var provider = Array.Find(Enum.GetValues<StudentInformationSystemProvider>(), x => x != StudentInformationSystemProvider.ThesisElements);
        Assert.Equal(100m, FinancialAidRules.BillApprovalAmount(Award() with { Provider = provider }));
    }

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
    [Theory]
    [InlineData(true,null,100,false,"Awarded",true)]
    [InlineData(true,1,100,false,"Awarded",false)]
    [InlineData(true,0,100,false,"Awarded",false)]
    [InlineData(false,null,100,false,"Awarded",false)]
    [InlineData(null,null,100,false,"Awarded",false)]
    [InlineData(true,null,0,false,"Awarded",false)]
    [InlineData(true,null,-100,false,"Awarded",false)]
    [InlineData(true,null,100,true,"Awarded",false)]
    [InlineData(true,null,100,false,"Cancelled",false)]
    public void Pending_loan_notice_excludes_accepted_declined_unknown_nonloan_billed_and_cancelled_awards(
        bool? loan,int? decision,int amount,bool billed,string status,bool expected)
    {
        var award=Award() with {IsLoan=loan,StudentAccepted=decision,Amount=amount,IsSentToBilling=billed,AwardStatus=status};
        Assert.Equal(expected,FinancialAidRules.IsLoanAwaitingAcceptance(award));
        if(expected)Assert.False(FinancialAidRules.IncludeInBillApproval(award));
    }
    [Fact]
    public void A_locked_pending_loan_can_be_explained_without_offering_an_acceptance_action()
    {
        var award=Award() with {IsLoan=true,IsLocked=true};
        Assert.True(FinancialAidRules.IsLoanAwaitingAcceptance(award));
        Assert.False(FinancialAidRules.CanDecide(award));
    }
    static StudentFinancialAidAward Award()=>new(StudentInformationSystemProvider.ThesisElements,"1","269","B27C","Spring 2027",DateTime.Today,"Grant","Guarantee","Grant",100,false,null,null,false);
}
