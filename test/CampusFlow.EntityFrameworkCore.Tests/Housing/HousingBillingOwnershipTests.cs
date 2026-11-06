using System;
using CampusFlow.StudentInformationSystems;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class HousingBillingOwnershipTests
{
    [Theory][InlineData("")][InlineData("Manual housing adjustment")][InlineData("CFH-269-999-BASE")]
    public void Unrecognized_or_another_students_charge_is_never_deleted(string reference)=>Assert.Throws<InvalidOperationException>(()=>ElementsHousingBillingWriter.RequireOwnedReferences([reference],["CFH-269-123-BASE","CFH-269-123-SINGLE"]));
    [Fact]public void Only_the_exact_students_owned_references_can_be_reconciled()=>ElementsHousingBillingWriter.RequireOwnedReferences(["CFH-269-123-BASE","CFH-269-123-SINGLE"],["CFH-269-123-BASE","CFH-269-123-SINGLE"]);
}
