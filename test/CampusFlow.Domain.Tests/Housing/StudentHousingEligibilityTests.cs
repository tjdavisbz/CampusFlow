using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Domain.Tests.Housing;

public class StudentHousingEligibilityTests
{
    [Theory]
    [InlineData("Residential Undergraduate", "Main", null, true, true)]
    [InlineData("American Indian College", "Main", null, true, false)]
    [InlineData("American Indian College", "Oaks_MAIN", "Commuter", true, false)]
    [InlineData("Graduate", "Oaks_MAIN", null, true, true)]
    [InlineData("Graduate", "Main", "Regents", true, true)]
    [InlineData("Graduate", "Main", "Commuter", true, true)]
    [InlineData("Graduate", "Main", null, false, false)]
    [InlineData("Distance Education", "Main", null, false, false)]
    [InlineData(" residential undergraduate ", "Main", null, true, true)]
    [InlineData("Graduate", " oaks_main ", null, true, true)]
    public void Eligibility_uses_any_qualifying_condition_with_an_absolute_Aic_meal_exclusion(
        string attendance, string campus, string? building, bool housing, bool meal)
    {
        var result = new StudentHousingEligibility(attendance, campus, building);
        Assert.Equal(housing, result.HousingRequired);
        Assert.Equal(meal, result.MealPlanEligible);
        Assert.Equal(!string.IsNullOrWhiteSpace(building), result.HasAssignment);
    }

    [Theory]
    [InlineData("School of Ministry", "MAIN", "Oaks_MAIN", true, true)]
    [InlineData("Distance Education", "MAIN", " oaks_main ", true, true)]
    [InlineData("Graduate", "Oaks_MAIN", "MAIN", true, true)]
    [InlineData("Graduate", "MAIN", "Oaks_MA", false, false)]
    [InlineData("American Indian College", "MAIN", "Oaks_MAIN", true, false)]
    public void Oaks_campus_on_either_record_qualifies_without_changing_AIC_or_other_campuses(string attendance,string termCampus,string studentCampus,bool housing,bool meals)
    {var result=new StudentHousingEligibility(attendance,termCampus,null,studentCampus);Assert.Equal(housing,result.HousingRequired);Assert.Equal(meals,result.MealPlanEligible);}

    [Theory]
    [InlineData("Commuter", HousingChoice.Commuter)]
    [InlineData(" commuter dorm ", HousingChoice.Commuter)]
    [InlineData("Regents Senior Housing", HousingChoice.OnCampus)]
    [InlineData("Bridges Hall", HousingChoice.OnCampus)]
    public void Recorded_building_selects_the_plan_category(string building, HousingChoice expected)
    {
        Assert.Equal(expected, new StudentHousingEligibility("Graduate", "Main", building).AssignedChoice);
    }
}
