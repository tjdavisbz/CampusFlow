using System;
using CampusFlow.MealPlanTaxes;
using Xunit;
namespace CampusFlow.MealPlanTaxes;
public class MealPlanTaxRulesTests
{
    [Theory]
    [InlineData(2475,204.19)] [InlineData(2575,212.44)] [InlineData(2250,185.63)]
    [InlineData(2850,235.13)] [InlineData(315,25.99)] [InlineData(515,42.49)]
    [InlineData(715,58.99)] [InlineData(0,0)]
    public void Tax_matches_all_seven_documented_plans_and_no_plan(decimal amount,decimal tax)=>Assert.Equal(tax,MealPlanTaxRules.Amount(amount));
    [Theory][InlineData(-1)][InlineData(1000001)]public void Invalid_prices_are_rejected(decimal amount)=>Assert.Throws<ArgumentOutOfRangeException>(()=>MealPlanTaxRules.Amount(amount));
    [Fact]public void Durable_identity_is_per_student_term_and_assignment_and_uncertainty_cannot_downgrade_verification()
    {
        var target=new MealPlanTaxTarget(10,269,42,8,2475m);var row=new MealPlanTaxCharge(Guid.NewGuid(),Guid.NewGuid(),target);
        Assert.Equal("CFMT-269-10-42",row.Reference);Assert.True(row.Matches(target));Assert.False(row.Matches(target with{BaseAmount=2575m}));
        row.Uncertain();Assert.Equal("Needs review",row.State);row.Verify("Batch:123");row.Uncertain();Assert.Equal("Verified",row.State);
    }
}
