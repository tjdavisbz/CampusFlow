using System;
using System.Collections.Generic;
using CampusFlow.Housing;
using CampusFlow.MealPlanTaxes;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class MealPlanRemovalRulesTests
{
    private static readonly MealPlanRemovalTarget Target=new(10,269,42,14,"Plan",715m);
    private static readonly MealPlanTaxDestination Tax=new(200,261,8047);
    private static ElementsMealPlanRemovalGateway.ReportedRow Base()=>new(1,100,165,0,715,0,"","Meal Plan","Meal Plan Charges","Open",15,true);
    private static ElementsMealPlanRemovalGateway.ReportedRow SalesTax()=>new(2,200,261,8047,58.99m,0,"CFMT-269-10-42","Meal plan sales tax (8.25%)","CampusFlow meal-plan sales tax","Open",2,true);
    [Fact]public void Only_the_exact_base_and_assignment_tax_are_reviewable()
    {
        var rows=ElementsMealPlanRemovalGateway.Classify(Target,165,Tax,[Base(),SalesTax()]);
        Assert.Equal(2,rows.Count);Assert.Equal(715m,rows[0].Amount);Assert.Equal(58.99m,rows[1].Amount);
        Assert.Empty(ElementsMealPlanRemovalGateway.Classify(Target,165,Tax,[]));
    }
    [Theory][InlineData("credit")][InlineData("reference")][InlineData("description")][InlineData("batch")][InlineData("closed")][InlineData("module")][InlineData("amount")][InlineData("ar")][InlineData("term-based")]
    public void Manual_or_changed_base_charges_are_not_selected(string change)
    {
        var r=Base();r=change switch{
            "credit"=>r with{Credit=1},"reference"=>r with{Reference="manual"},"description"=>r with{Description="Manual adjustment"},
            "batch"=>r with{BatchComment="Other"},"closed"=>r with{BatchStatus="Closed"},"module"=>r with{SourceModule=1},
            "amount"=>r with{Debit=700},"ar"=>r with{ArType=3},_=>r with{TermBased=false}};
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target,165,Tax,[r]));
    }
    [Theory][InlineData("reference")][InlineData("batch")][InlineData("amount")][InlineData("doc")]
    public void Another_assignment_or_tax_identity_is_not_selected(string change)
    {
        var r=SalesTax();r=change switch{"reference"=>r with{Reference="CFMT-269-10-43"},"batch"=>r with{Batch=201},"amount"=>r with{Debit=59},_=>r with{Document=260}};
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target,165,Tax,[r]));
    }
    [Fact]public void Duplicate_matching_entries_require_review()
    {Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target,165,Tax,[Base(),Base() with{Id=3}]));}
}
