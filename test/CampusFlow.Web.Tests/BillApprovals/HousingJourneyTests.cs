using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.BillApprovals;

public class HousingJourneyTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    public void Dashboard_shows_only_eligible_steps_and_blocks_bill_until_housing_is_assigned(
        bool housingRequired, bool assigned, bool mealEligible, bool billBlocked)
    {
        var journey = new IndexModel.RegistrationJourneyViewModel
        {
            Term = new(StudentInformationSystemProvider.ThesisElements, "270", "B27S", "Spring 2027", new(2027, 1, 1), new(2027, 5, 1)),
            HousingRequired = housingRequired, HasHousingAssignment = assigned, MealPlanEligible = mealEligible,
            BillApprovalEnabled = true, MealPlanEnabled = true, MealPlanRequired = mealEligible
        };
        journey.BuildSteps();
        Assert.Equal(housingRequired, journey.Steps.Any(x => x.Title == "Housing"));
        Assert.Equal(mealEligible, journey.Steps.Any(x => x.Title == "Meal plan"));
        var bill = journey.Steps.Single(x => x.Title == "Bill approval");
        Assert.Equal(billBlocked, bill.Url is null);
        if (housingRequired) Assert.Contains("term=270", journey.Steps.Single(x => x.Title == "Housing").Url);
    }
}
