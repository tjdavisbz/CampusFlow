using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using Xunit;
using Entry=CampusFlow.StudentInformationSystems.ElementsHousingBillingWriter.BatchEntry;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class HousingLegacyChargeOwnershipTests
{
    private static Entry Legacy()=>new(100,200,"",137,2750,10,269,8047,"Housing");
    [Fact] public void Only_the_exact_unchanged_import_entry_can_be_adopted()
    {var row=Legacy();var review=ElementsHousingBillingWriter.ReviewOwnership([row],[row],10,269,200,[137,243]);Assert.Equal(row,Assert.Single(review.Legacy));}
    [Theory][InlineData("amount")][InlineData("account")][InlineData("document")][InlineData("description")][InlineData("reference")][InlineData("id")][InlineData("no-evidence")]
    public void Manual_or_changed_blank_reference_entries_are_never_adopted(string problem)
    {
        var row=problem switch{"amount"=>Legacy() with{Amount=1},"account"=>Legacy() with{AccountsReceivableTypeId=1},"document"=>Legacy() with{TransactionDocumentId=243},"description"=>Legacy() with{Description="Manual adjustment"},"reference"=>Legacy() with{Reference="Manual"},"id"=>Legacy() with{Id=101},_=>Legacy()};
        Assert.Throws<InvalidOperationException>(()=>ElementsHousingBillingWriter.ReviewOwnership([row],problem=="no-evidence"?[]:[Legacy()],10,269,200,[137,243]));
    }
    [Fact] public void Duplicate_base_charges_are_not_silently_merged()
    {var row=Legacy();var owned=row with{Id=101,Reference="CFH-269-10-BASE"};Assert.Throws<InvalidOperationException>(()=>ElementsHousingBillingWriter.ReviewOwnership([row,owned],[row],10,269,200,[137]));}
    [Fact] public void Multiple_imported_base_charges_require_manual_review()
    {var a=Legacy();var b=a with{Id=101};Assert.Throws<InvalidOperationException>(()=>ElementsHousingBillingWriter.ReviewOwnership([a,b],[a,b],10,269,200,[137]));}
    [Theory][InlineData("student")][InlineData("batch")][InlineData("credit")][InlineData("duplicate-id")]
    public void Out_of_scope_credits_and_ambiguous_rows_are_blocked(string problem)
    {
        var row=problem switch{"student"=>Legacy() with{StudentUid=11},"batch"=>Legacy() with{BatchMasterId=201},"credit"=>Legacy() with{Amount=-1},_=>Legacy()};
        Assert.Throws<InvalidOperationException>(()=>ElementsHousingBillingWriter.ReviewOwnership(problem=="duplicate-id"?[row,row]:[row],[row],10,269,200,[137]));
    }
    [Fact] public void Exact_owned_references_remain_idempotent_and_other_terms_are_preserved()
    {var owned=Legacy() with{Reference="CFH-269-10-BASE"};var other=Legacy() with{Id=101,TermCalendarId=268};var review=ElementsHousingBillingWriter.ReviewOwnership([owned,other],[],10,269,200,[137]);Assert.Empty(review.Legacy);Assert.Equal(owned,Assert.Single(review.Entries));}
}
