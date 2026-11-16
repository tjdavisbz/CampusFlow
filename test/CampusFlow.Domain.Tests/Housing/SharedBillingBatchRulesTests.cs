using System;
using CampusFlow.Housing;
using Xunit;
namespace CampusFlow.Housing;
public class SharedBillingBatchRulesTests
{
    static SharedBillingBatchIdentity Batch(int id=10,string comment="Meal Plan Charges",string status="Open",int module=2,bool term=true)=>new(id,comment,status,module,term);
    [Fact]public void An_existing_approved_destination_remains_preferred()=>
        Assert.Equal(10,SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[Batch(),Batch(20)]));
    [Theory][InlineData("Meal Plan Charges")][InlineData("Housing Charges")]
    public void A_unique_approved_replacement_is_found_after_distribution_deletes_the_old_header(string comment)=>
        Assert.Equal(20,SharedBillingBatchRules.Resolve(10,comment,[Batch(20,comment)]));
    [Fact]public void No_destination_for_new_charges_is_needed_to_review_removal_of_old_charges()=>
        Assert.Equal(10,SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[],false));
    [Fact]public void A_missing_write_destination_stops_before_student_changes()=>
        Assert.Throws<InvalidOperationException>(()=>SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[]));
    [Fact]public void Multiple_possible_replacements_require_review()=>
        Assert.Throws<InvalidOperationException>(()=>SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[Batch(20),Batch(30)]));
    [Theory][InlineData("Other charges",2,true)][InlineData("Meal Plan Charges",15,true)][InlineData("Meal Plan Charges",2,false)]
    public void A_repurposed_configured_header_cannot_be_silently_replaced(string comment,int module,bool term)=>
        Assert.Throws<InvalidOperationException>(()=>SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[Batch(10,comment,"Open",module,term),Batch(20)]));
    [Fact]public void A_closed_header_is_not_used_for_new_charges()=>
        Assert.Throws<InvalidOperationException>(()=>SharedBillingBatchRules.Resolve(10,"Meal Plan Charges",[Batch(status:"Closed")]));
}
