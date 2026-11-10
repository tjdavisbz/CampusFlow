using System;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.Domain.Tests.Housing;

public class MealPlanCatalogRulesTests
{
    private static readonly StudentMealPlanTerm Spring = new(269, "Spring 2027", new(2027,1,11), new(2027,4,30));
    private static MealPlanConfiguration Config(string name, int? id=null) =>
        new(Guid.NewGuid(),null,id,name,name,"Dining","[1,2,3]","[]",100,10);
    private static StudentMealPlanCatalogItem Plan(string name, int id=10) =>
        new(id,name,"Dining",100,Spring.StartDate,Spring.EndDate);

    [Theory]
    [InlineData("12 Meal Plan","12 Meal Plan - Spr 27")]
    [InlineData("15 Meal Plan","15 Meal Plan - Spr 27")]
    [InlineData("120 Meal Plan","120 Block - Spr 27")]
    [InlineData("Unlimited Meal Plan","Unlimited Meals - Spr 27")]
    [InlineData("20 Meal Plan","20 Block Meal Plan - Spr 27")]
    [InlineData("40 Meal Plan","40 Block Meal Plan - Spr 27")]
    [InlineData("60 Meal Plan","60 Block Meal Plan - Spr 27")]
    [InlineData("120 Meal Plan","120 Block")]
    [InlineData("Unlimited Meal Plan","Unlimited Meals")]
    [InlineData("20 Meal Plan","20 Block Meal Plan")]
    [InlineData("40 Meal Plan","40 Block Meal Plan")]
    [InlineData("60 Meal Plan","60 Block Meal Plan")]
    [InlineData("12 Meal Plan"," 12 meal plan - Spring 2027 ")]
    public void Reviewed_names_resolve_to_the_actual_selected_term_id(string name,string externalName) =>
        Assert.Equal(10,MealPlanCatalogRules.Resolve(Config(name),[Plan(externalName)],Spring)?.MealPlanId);

    [Theory]
    [InlineData("12 Meal Plan","120 Block - Spr 27")]
    [InlineData("20 Meal Plan","120 Block - Spr 27")]
    [InlineData("20 Meal Plan","20 Block Meal Plan - Spr 26")]
    [InlineData("20 Meal Plan","20 Block Meal Plan - Fall 27")]
    [InlineData("20 Meal Plan","20 Block Meal Plan - Spr 28")]
    [InlineData("20 Meal Plan","Special 20 Block Meal Plan")]
    [InlineData("None","NO MEALS - OFFICE ONLY")]
    public void Partial_names_wrong_term_suffixes_and_office_exemptions_are_not_substituted(string name,string externalName) =>
        Assert.Null(MealPlanCatalogRules.Resolve(Config(name),[Plan(externalName)],Spring));

    [Fact]
    public void Matching_names_outside_the_selected_term_cannot_be_used() =>
        Assert.Null(MealPlanCatalogRules.Resolve(Config("12 Meal Plan"),
            [Plan("12 Meal Plan") with {StartDate=new(2026,8,24),EndDate=new(2026,12,11)}],Spring));
    [Fact]
    public void Ambiguous_active_candidates_require_review_instead_of_silent_selection() =>
        Assert.Null(MealPlanCatalogRules.Resolve(Config("20 Meal Plan"),
            [Plan("20 Meal Plan"),Plan("20 Block Meal Plan - Spr 27",11)],Spring));
    [Fact]
    public void An_explicit_mapping_is_never_replaced_by_a_different_plan_id()
    {
        Assert.Null(MealPlanCatalogRules.Resolve(Config("20 Meal Plan",99),[Plan("20 Block Meal Plan - Spr 27")],Spring));
        Assert.Equal(11,MealPlanCatalogRules.Resolve(Config("20 Meal Plan",11),
            [Plan("20 Meal Plan"),Plan("20 Block Meal Plan - Spr 27",11)],Spring)?.MealPlanId);
    }
}
