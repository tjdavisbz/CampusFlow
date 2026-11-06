using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalJourneyControlsTests
{
    private static IndexModel.RegistrationJourneyViewModel Journey() => new()
    {
        Term = new(StudentInformationSystemProvider.ThesisElements, "269", "B27S", "Spring 2027", new(2027, 1, 11), new(2027, 4, 30)),
        HousingRequired = true, HasHousingAssignment = true, MealPlanEligible = true,
        MealPlanRequired = true, MealPlanEnabled = true, CourseSelectionEnabled = true
    };

    [Fact]
    public void Approved_terms_remain_complete_after_closure_and_student_changes_are_locked()
    {
        var journey = Journey();journey.BillApproved = true;journey.BillApprovalEnabled = false;
        journey.BuildSteps();
        Assert.Equal(journey.Steps.Count, journey.CompletedCount);
        Assert.All(journey.Steps.Where(x => x.Title != "Bill approval"), x => Assert.Null(x.Url));
        Assert.Contains("BillApproval", journey.Steps.Single(x => x.Title == "Bill approval").Url);
    }

    [Theory]
    [InlineData("upcoming", "Coming soon")]
    [InlineData("closed", "Closed")]
    [InlineData("unavailable", "Unavailable")]
    public void Unavailable_bill_and_meal_steps_use_the_actual_term_state(string state, string label)
    {
        var journey = Journey();journey.BillApprovalAvailability = state;journey.BuildSteps();
        Assert.Equal(label, journey.Steps.Single(x => x.Title == "Bill approval").ActionLabel);
        Assert.Equal(label, journey.Steps.Single(x => x.Title == "Meal plan").ActionLabel);
    }

    [Fact]
    public void Previous_balance_blocks_bill_approval_but_keeps_course_and_meal_selection_links()
    {
        var journey = Journey();journey.BillApprovalEnabled = true;journey.PreviousBalance = 100;
        journey.BuildSteps();
        Assert.Null(journey.Steps.Single(x => x.Title == "Bill approval").Url);
        Assert.NotNull(journey.Steps.Single(x => x.Title == "Course selection").Url);
        Assert.NotNull(journey.Steps.Single(x => x.Title == "Meal plan").Url);
    }
}
