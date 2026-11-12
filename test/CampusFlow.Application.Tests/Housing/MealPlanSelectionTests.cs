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
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Guids;
using Xunit;

namespace CampusFlow.Application.Tests.Housing;

public class MealPlanSelectionTests
{
    private readonly IStudentInformationSystemMealPlanService _sis = Substitute.For<IStudentInformationSystemMealPlanService>();
    private readonly IElementsHousingReader _housing = Substitute.For<IElementsHousingReader>();
    private readonly MealPlanAppService _service;
    private readonly IBillApprovalCompletionLookup _completion = Substitute.For<IBillApprovalCompletionLookup>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IRepository<StudentHousingSelection, Guid> _selections = Substitute.For<IRepository<StudentHousingSelection, Guid>>();
    private readonly IStudentInformationSystemMealPlanRemovalService _assignments = Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
    private StudentHousingSelection? _stored;

    private readonly IMealPlanRemovalCoordinator _removals=MealPlanRemovalTestDouble.Create();
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
        _assignments.GetAssignmentsAsync("10", Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentMealPlanAssignment>());
        _selections.FindAsync(Arg.Any<Expression<Func<StudentHousingSelection, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_stored));
        _selections.InsertAsync(Arg.Any<StudentHousingSelection>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => { _stored = call.Arg<StudentHousingSelection>(); return Task.FromResult(_stored); });
        _sis.AssignAsync("10", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _assignments.GetAssignmentsAsync("10", Arg.Any<CancellationToken>()).Returns(
                new StudentMealPlanAssignment[] { new(100, call.Arg<int>(), 270, "Selected plan", 315) });
            return Task.CompletedTask;
        });
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        var guids = Substitute.For<IGuidGenerator>(); guids.Create().Returns(_ => Guid.NewGuid());
        lazy.LazyGetRequiredService<IGuidGenerator>().Returns(guids);
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(Substitute.For<ICurrentTenant>());
        _service = new(Substitute.For<IRepository<StudentProfile, Guid>>(), current, configurations,
            _selections, new[] { _sis }, NullLogger<MealPlanAppService>.Instance,
            _housing, terms, _clock, _completion, _assignments, _removals);
        _service.LazyServiceProvider = lazy;
    }

    [Fact]
    public async Task Pending_administrative_removal_blocks_selection_until_cleanup_is_verified()
    {
        Eligibility("Residential Undergraduate","Commuter");
        _removals.PendingAsync(10,270).Returns(new List<MealPlanRemovalTarget>{new(10,270,42,3,"Plan",315)});
        var view=await _service.GetAsync("270");
        Assert.True(view.HasPendingRemoval);Assert.False(view.IsSelectionOpen);Assert.Null(view.SelectedHousingChoice);Assert.Contains("Business Services review",view.UnavailableReason);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.SaveAsync(new(){ExternalTermId="270",HousingChoice=HousingChoice.Commuter,ExternalMealPlanId=3}));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!,default,default);
    }

    private void Eligibility(string attendance, string? building) =>
        _housing.GetStudentEligibilityAsync(10, 270, Arg.Any<CancellationToken>())
            .Returns(new StudentHousingEligibility(attendance, "Main", building));

    private void StoredPaidPlan(bool synced = true)
    {
        _stored = new(Guid.NewGuid(), null, Guid.NewGuid(), "10", "January 2027",
            new(2027, 1, 1), new(2027, 5, 1), HousingChoice.Commuter, 3, "20 Meal Plan");
        if (synced) _stored.MarkSynced();
        else _stored.MarkPending("Assignment not confirmed");
        Eligibility("Residential Undergraduate", "Commuter");
    }

    [Fact]
    public async Task Previously_removed_paid_plan_is_not_shown_but_GET_retains_its_audit_record()
    {
        StoredPaidPlan();
        var view = await _service.GetAsync("270");
        Assert.Null(view.SelectedHousingChoice);
        Assert.Null(view.SelectedMealPlanId);
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
    }

    [Fact]
    public async Task Removed_paid_choice_can_be_replaced_after_live_recheck()
    {
        StoredPaidPlan();
        var original = _stored;
        var result = await _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, SelectNoMealPlan = true });
        Assert.Null(result.SelectedMealPlanId);
        Assert.Equal("None", result.SelectedMealPlanName);
        await _selections.Received(1).DeleteAsync(original!, true, Arg.Any<CancellationToken>());
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
    }

    [Fact]
    public async Task An_existing_Elements_plan_blocks_reselection_even_without_a_local_choice()
    {
        Eligibility("Residential Undergraduate", "Commuter");
        _assignments.GetAssignmentsAsync("10", Arg.Any<CancellationToken>()).Returns(
            new StudentMealPlanAssignment[] { new(100, 3, 270, "20 Meal Plan", 315) });
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, SelectNoMealPlan = true }));
        await _selections.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task A_plan_in_another_term_does_not_make_the_removed_choice_current()
    {
        StoredPaidPlan();
        _assignments.GetAssignmentsAsync("10", Arg.Any<CancellationToken>()).Returns(
            new StudentMealPlanAssignment[] { new(100, 3, 269, "Other term", 315) });
        Assert.Null((await _service.GetAsync("270")).SelectedHousingChoice);
    }

    [Fact]
    public async Task Unconfirmed_assignment_is_never_automatically_discarded()
    {
        StoredPaidPlan(synced: false);
        Assert.Equal(3, (await _service.GetAsync("270")).SelectedMealPlanId);
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, SelectNoMealPlan = true }));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
    }

    [Fact]
    public async Task Unavailable_assignment_read_cannot_clear_a_choice_or_enable_reselection()
    {
        StoredPaidPlan();
        _assignments.GetAssignmentsAsync("10", Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<StudentMealPlanAssignment>>(new InvalidOperationException("Unavailable")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetAsync("270"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, SelectNoMealPlan = true }));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
    }

    [Fact]
    public async Task No_choice_is_rejected_without_recording_None()
    {
        Eligibility("Residential Undergraduate", "Commuter");
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter }));
        await _selections.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
    }

    [Fact]
    public async Task Explicit_None_is_recorded_without_assigning_a_fake_Elements_plan()
    {
        Eligibility("Residential Undergraduate", "Commuter");
        var result = await _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, SelectNoMealPlan = true });
        Assert.Equal(HousingChoice.Commuter, result.SelectedHousingChoice);
        Assert.Equal("None", result.SelectedMealPlanName);
        Assert.Null(result.SelectedMealPlanId);
        Assert.True(result.SyncedToStudentInformationSystem);
        await _selections.Received(1).InsertAsync(Arg.Is<StudentHousingSelection>(x => x.ExternalMealPlanId == null), true, Arg.Any<CancellationToken>());
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(3, true)]
    public async Task Missing_invalid_or_conflicting_choices_do_not_write(int? id, bool none)
    {
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.Commuter, ExternalMealPlanId = id, SelectNoMealPlan = none }));
        await _selections.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
    }

    [Fact]
    public async Task Explicit_None_cannot_bypass_a_residential_catalog()
    {
        Eligibility("Residential Undergraduate", "Bridges");
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new()
            { ExternalTermId = "270", HousingChoice = HousingChoice.OnCampus, SelectNoMealPlan = true }));
        await _selections.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

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
    public async Task Approved_bill_locks_direct_meal_plan_submissions()
    {
        Eligibility("Residential Undergraduate", "Bridges");
        _completion.IsApprovedAsync(Arg.Any<StudentProfile>(), "270", Arg.Any<CancellationToken>()).Returns(true);
        var view = await _service.GetAsync("270");
        Assert.True(view.IsLockedByBillApproval);
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.SaveAsync(new() { ExternalTermId = "270", HousingChoice = HousingChoice.OnCampus, ExternalMealPlanId = 2 }));
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }

    [Fact]
    public async Task Unconfigured_term_is_rejected_before_any_Elements_calls()
    {
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.GetAsync("999"));
        await _sis.DidNotReceiveWithAnyArgs().GetContextAsync(default!, default);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!, default);
    }
    [Fact]
    public async Task Actual_Spring_Elements_names_resolve_and_keep_commuter_choices_separate()
    {
        Eligibility("Graduate", "Teeter Hall");
        var context = await _sis.GetContextAsync("10",270);
        _sis.GetContextAsync("10",270,Arg.Any<CancellationToken>()).Returns(context with
        {
            Catalog = new[] {
                new StudentMealPlanCatalogItem(9,"12 Meal Plan - Spr 27","Dining",2475,new(2027,1,1),new(2027,5,1)),
                new StudentMealPlanCatalogItem(10,"20 Block Meal Plan - Spr 27","Dining",315,new(2027,1,1),new(2027,5,1)) }
        });
        var view=await _service.GetAsync("270");
        Assert.Equal(9,Assert.Single(view.Options[HousingChoice.OnCampus]).ExternalMealPlanId);
        Assert.Empty(view.Options[HousingChoice.Commuter]);
        Assert.Null(view.UnavailableReason);
    }
    [Fact]
    public async Task Missing_term_catalog_explains_the_setup_problem_and_does_not_offer_a_previous_term_plan()
    {
        Eligibility("Graduate", "Teeter Hall");
        var context=await _sis.GetContextAsync("10",270);
        _sis.GetContextAsync("10",270,Arg.Any<CancellationToken>()).Returns(context with {Catalog=Array.Empty<StudentMealPlanCatalogItem>()});
        var view=await _service.GetAsync("270");
        Assert.All(view.Options.Values,x=>Assert.Empty(x));
        Assert.Contains("not yet available",view.UnavailableReason);
        await _sis.DidNotReceiveWithAnyArgs().AssignAsync(default!,default);
    }
    [Fact]
    public async Task Closed_access_explains_that_the_selection_window_has_closed()
    {
        Eligibility("Graduate", "Teeter Hall");_clock.Now.Returns(new DateTime(2027,3,1));
        Assert.Contains("window for this term has closed",(await _service.GetAsync("270")).UnavailableReason);
    }
    [Fact]
    public async Task Future_access_explains_that_selection_uses_the_bill_approval_window()
    {
        Eligibility("Graduate", "Teeter Hall");_clock.Now.Returns(new DateTime(2026,10,1));
        Assert.Contains("opens with bill approval",(await _service.GetAsync("270")).UnavailableReason);
    }
}
