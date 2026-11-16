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
    [Fact]public void A_verified_zero_dollar_base_entry_is_reviewable_for_No_Meals()
    {
        var target=Target with{Name="No Meals",Amount=0};
        var entry=Assert.Single(ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[Base() with{Debit=0}]));
        Assert.Equal(0,entry.Amount);Assert.Equal("meal plan",entry.Kind);
        Assert.Empty(ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[]));
    }
    [Theory][InlineData("credit")][InlineData("reference")][InlineData("description")][InlineData("batch")]
    [InlineData("closed")][InlineData("module")][InlineData("amount")][InlineData("ar")][InlineData("term-based")]
    public void Zero_dollar_entries_keep_the_same_ownership_and_billing_guards(string change)
    {
        var row=Base() with{Debit=0};row=change switch
        {
            "credit"=>row with{Credit=1},"reference"=>row with{Reference="manual"},
            "description"=>row with{Description="Manual adjustment"},"batch"=>row with{BatchComment="Other"},
            "closed"=>row with{BatchStatus="Closed"},"module"=>row with{SourceModule=1},
            "amount"=>row with{Debit=715},"ar"=>row with{ArType=3},_=>row with{TermBased=false}
        };
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target with{Name="No Meals",Amount=0},165,Tax,[row]));
    }
    [Fact]public void No_Meals_never_authorizes_a_paid_charge_tax_or_duplicate_zero_entries()
    {
        var target=Target with{Name="No Meals",Amount=0};
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[Base()]));
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[SalesTax()]));
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[SalesTax() with{Debit=0}]));
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(target,165,Tax,[Base() with{Debit=0},Base() with{Id=3,Debit=0}]));
    }
    [Fact]public void Shared_meal_batch_and_legacy_tax_remain_reviewable_without_accepting_other_cashier_batches()
    {
        var destination=new MealPlanTaxDestination(300,261,8047,300,200);
        var meal=Base() with{Batch=300,SourceModule=2};
        var tax=SalesTax() with{Batch=300,BatchComment="Meal Plan Charges"};
        Assert.Equal(2,ElementsMealPlanRemovalGateway.Classify(Target,165,destination,[meal,tax]).Count);
        Assert.Equal(2,ElementsMealPlanRemovalGateway.Classify(Target,165,destination,[Base(),SalesTax()]).Count);
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target,165,destination,[meal with{Batch=301}]));
        Assert.Throws<InvalidOperationException>(()=>ElementsMealPlanRemovalGateway.Classify(Target,165,destination,[tax with{Batch=301}]));
    }
}
