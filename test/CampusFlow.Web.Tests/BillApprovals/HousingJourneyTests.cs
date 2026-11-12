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
            BillApprovalEnabled = true, MealPlanEnabled = true, MealPlanRequired = mealEligible, MealPlanSelected = true
        };
        journey.BuildSteps();
        Assert.Equal(housingRequired, journey.Steps.Any(x => x.Title == "Housing"));
        Assert.Equal(mealEligible, journey.Steps.Any(x => x.Title == "Meal plan"));
        var bill = journey.Steps.Single(x => x.Title == "Bill approval");
        Assert.Equal(billBlocked, bill.Url is null);
        if (housingRequired) Assert.Contains("term=270", journey.Steps.Single(x => x.Title == "Housing").Url);
    }
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void Bill_step_requires_a_choice_only_for_meal_eligible_unapproved_students(bool approved,bool eligible,bool selected)
    {
        var journey=new IndexModel.RegistrationJourneyViewModel {
            Term=new(StudentInformationSystemProvider.ThesisElements,"269","B27C","Spring 2027",new(2027,1,11),new(2027,4,30)),
            BillApproved=approved,BillApprovalEnabled=true,HasHousingAssignment=true,MealPlanEligible=eligible,MealPlanSelected=selected};
        journey.BuildSteps();var bill=journey.Steps.Single(x=>x.Title=="Bill approval");
        Assert.Equal(!approved&&eligible&&!selected,bill.Url is null);
    }
    [Theory]
    [InlineData("Meal plans are not yet available for your assigned housing in this term.")]
    [InlineData("Meal plan selection opens with bill approval for this term.")]
    public void Meal_step_explains_its_own_blocker_without_unlocking_selection(string reason)
    {
        var journey=new IndexModel.RegistrationJourneyViewModel {
            Term=new(StudentInformationSystemProvider.ThesisElements,"269","B27C","Spring 2027",new(2027,1,11),new(2027,4,30)),
            HousingRequired=true,HasHousingAssignment=true,MealPlanEligible=true,BillApprovalEnabled=true,
            MealPlanEnabled=false,MealPlanUnavailableReason=reason};
        journey.BuildSteps();var step=journey.Steps.Single(x=>x.Title=="Meal plan");
        Assert.Equal(reason,step.Description);Assert.Null(step.Url);
    }
}
