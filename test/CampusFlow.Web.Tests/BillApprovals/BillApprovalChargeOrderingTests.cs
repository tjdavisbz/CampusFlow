using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.BillApprovals;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalChargeOrderingTests
{
    private static StudentBillingTransaction Charge(string id,string description,decimal amount=100,
        int day=16,bool voided=false) => new(StudentInformationSystemProvider.ThesisElements,id,"269","B27C",
            "Spring 2027",new DateTime(2026,11,day),description,Math.Max(amount,0),Math.Max(-amount,0),amount,true,voided);

    [Fact]public void Tuition_is_first_and_other_descriptions_are_alphabetical_with_meal_and_tax_adjacent()
    {
        var charges=new[]{Charge("1","Housing — Savell Hall",1525),Charge("2","Meal plan sales tax (8.25%)",212.44m),
            Charge("3","Tuition",13314),Charge("4","General Fee",520),Charge("5","Technology Fee"),Charge("6","Meal Plan",2575)};
        var ordered=BillApprovalChargeOrdering.ForDisplay(charges);
        Assert.Equal(new[]{"Tuition","General Fee","Housing — Savell Hall","Meal Plan","Meal plan sales tax (8.25%)","Technology Fee"},ordered.Select(x=>x.Description));
        Assert.Equal(18246.44m,ordered.Sum(x=>x.BalanceChange));
        Assert.Equal(new[]{"1","2","3","4","5","6"},charges.Select(x=>x.ExternalTransactionId));
        Assert.All(charges,x=>Assert.Same(x,ordered.Single(y=>y.ExternalTransactionId==x.ExternalTransactionId)));
    }
    [Fact]public void Case_spacing_tuition_variants_and_equal_descriptions_have_deterministic_order()
    {
        var ordered=BillApprovalChargeOrdering.ForDisplay([Charge("2","Meal Plan",day:15),Charge("3","Meal Plan",day:14),
            Charge("1","Meal Plan",day:15),Charge("4"," tuition - Undergraduate"),Charge("5","Non-Tuition Fee"),Charge("6","general fee")]);
        Assert.Equal(new[]{"4","6","3","1","2","5"},ordered.Select(x=>x.ExternalTransactionId));
    }
    [Fact]public void Credits_and_pending_flags_remain_intact_and_voided_rows_stay_hidden()
    {
        var credit=Charge("1","Account credit",-75);var pending=Charge("2","Tuition",100);
        var result=BillApprovalChargeOrdering.ForDisplay([credit,Charge("3","Tuition",999,voided:true),pending]);
        Assert.Equal(new[]{pending,credit},result);Assert.Equal(25,result.Sum(x=>x.BalanceChange));
        Assert.True(result[0].IsPending);Assert.Equal(75,result[1].Credit);
        Assert.Empty(BillApprovalChargeOrdering.ForDisplay([]));
    }
}
