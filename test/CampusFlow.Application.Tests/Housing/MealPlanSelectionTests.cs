using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Xunit;

namespace CampusFlow.Application.Tests.Housing;

public class MealPlanSelectionTests
{
    private readonly IStudentInformationSystemMealPlanService _sis = Substitute.For<IStudentInformationSystemMealPlanService>();
    private readonly IElementsHousingReader _housing = Substitute.For<IElementsHousingReader>();
    private readonly MealPlanAppService _service;
    private readonly IClock _clock = Substitute.For<IClock>();

    public MealPlanSelectionTests()
    {
        var profile = new StudentProfile(Guid.NewGuid(), null, Guid.NewGuid(),
            new(StudentInformationSystemProvider.ThesisElements, "10", "100", "student@example.test", "Test", null, "Student"));
        var current = Substitute.For<ICurrentStudentView>();
        current.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        var terms = Substitute.For<IRepository<BillApprovalTermConfiguration, Guid>>();
        terms.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<BillApprovalTermConfiguration>
        {
            new(Guid.NewGuid(), null, "270", "B27S", "Spring 2027", new(2026, 11, 1), new(2027, 2, 1), true, Guid.NewGuid(), Guid.NewGuid())
        });
        _clock.Now.Returns(new DateTime(2026, 11, 5));
        var configurations = Substitute.For<IRepository<MealPlanConfiguration, Guid>>();
        configurations.GetListAsync(Arg.Any<Expression<Func<MealPlanConfiguration, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<MealPlanConfiguration>
            {
                new(Guid.NewGuid(), null, null, "12 Meal Plan", "12 Meal Plan", "Residential", "[1,2]", "[]", 2679.19m, 10, false),
                new(Guid.NewGuid(), null, null, "20 Meal Plan", "20 Meal Plan", "Commuter", "[3]", "[]", 340.99m, 20, false),
                new(Guid.NewGuid(), null, null, "None", "None", "No plan", "[3]", "[]", 0m, 30, true)
            });
        _sis.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _sis.GetContextAsync("10", 270, Arg.Any<CancellationToken>()).Returns(new StudentMealPlanContext("Graduate",
            new StudentMealPlanCatalogItem[]
            {
                new(1, "12 Meal Plan", "Fall", 1m, new(2026, 8, 1), new(2026, 12, 1)),
                new(2, "12 Meal Plan", "Spring", 2475m, new(2027, 1, 1), new(2027, 5, 1)),
                new(3, "20 Meal Plan", "Spring", 315m, new(2027, 1, 1), new(2027, 5, 1)),
                new(4, "12 Meal Plan", "Future", 1m, new(2028, 1, 1), new(2028, 5, 1))
            }, new StudentMealPlanTerm[] { new(270, "Spring 2027", new(2027, 1, 1), new(2027, 5, 1)) }, 270, Array.Empty<StudentHousingStatusOption>(), null));
        _service = new(Substitute.For<IRepository<StudentProfile, Guid>>(), current, configurations,
            Substitute.For<IRepository<StudentHousingSelection, Guid>>(), new[] { _sis }, NullLogger<MealPlanAppService>.Instance,
            _housing, terms, _clock);
    }

    private void Eligibility(string attendance, string? building) =>
        _housing.GetStudentEligibilityAsync(10, 270, Arg.Any<CancellationToken>())
            .Returns(new StudentHousingEligibility(attendance, "Main", building));

    [Fact]
    public async Task Residential_options_use_selected_term_and_tax_inclusive_configured_price()
    {
        Eligibility("Graduate", "Regents");
        var result = await _service.GetAsync("270");
        var option = Assert.Single(result.Options[HousingChoice.OnCampus]);
        Assert.Equal(2, option.ExternalMealPlanId);
        Assert.Equal(2679.19m, option.Price);
        Assert.Empty(result.Options[HousingChoice.Commuter]);
        await _housing.Received(1).GetStudentEligibilityAsync(10, 270, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commuter_options_include_none_and_exclude_full_plans()
    {
        Eligibility("Residential Undergraduate", "Commuter");
        var result = await _service.GetAsync("270");
        Assert.Equal(2, result.Options[HousingChoice.Commuter].Count);
        Assert.Contains(result.Options[HousingChoice.Commuter], x => x.IsNoPlanOption && x.Price == 0m);
        Assert.Empty(result.Options[HousingChoice.OnCampus]);
    }

    [Theory]
    [InlineData("American Indian College", "Bridges")]
    [InlineData("Graduate", null)]
    [InlineData("Residential Undergraduate", null)]
    public async Task Ineligible_or_unassigned_students_cannot_save(string attendance, string? building)
    {
        Eligibility(attendance, building);
        var view = await _service.GetAsync("270");
        Assert.All(view.Options.Values, x => Assert.Empty(x));
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new() { ExternalTermId = "270", HousingChoice = HousingChoice.OnCampus, ExternalMealPlanId = 2 }));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }

    [Fact]
    public async Task Forged_commuter_choice_is_rejected_for_a_residential_assignment()
    {
        Eligibility("Residential Undergraduate", "Bridges");
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new() { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter }));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }

    [Fact]
    public async Task Expired_selection_window_cannot_be_bypassed_with_a_direct_save()
    {
        Eligibility("Residential Undergraduate", "Bridges");
        _clock.Now.Returns(new DateTime(2027, 3, 1));
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new() { ExternalTermId = "270", HousingChoice = HousingChoice.OnCampus, ExternalMealPlanId = 2 }));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }

    [Fact]
    public async Task A_plan_from_another_term_cannot_be_submitted()
    {
        Eligibility("Residential Undergraduate", "Bridges");
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new() { ExternalTermId = "270", HousingChoice = HousingChoice.OnCampus, ExternalMealPlanId = 4 }));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }

    [Fact]
    public async Task Unconfigured_term_is_rejected_before_any_Elements_calls()
    {
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.GetAsync("999"));
        await _sis.DidNotReceiveWithAnyArgs().GetContextAsync(default!, default);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }
}
