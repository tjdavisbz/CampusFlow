using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.Application.Tests.Housing;

public class AdminNoMealPlanSelectionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IRepository<StudentHousingSelection, Guid> _selections = Substitute.For<IRepository<StudentHousingSelection, Guid>>();
    private readonly IStudentInformationSystemMealPlanService _plans = Substitute.For<IStudentInformationSystemMealPlanService>();
    private readonly IStudentInformationSystemMealPlanRemovalService _removal = Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
    private readonly StudentHousingSelection _selection;
    private readonly AdminMealPlanAppService _service;
    private readonly List<StudentHousingSelection> _rows = [];

    public AdminNoMealPlanSelectionTests()
    {
        _selection = new(Guid.NewGuid(), _tenantId, Guid.NewGuid(), "123", "January 2027",
            new(2027, 1, 11), new(2027, 4, 30), HousingChoice.Commuter, null, "None");
        _selection.MarkSynced(); _rows.Add(_selection);
        _selections.GetListAsync(Arg.Any<Expression<Func<StudentHousingSelection, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => _rows.Where(call.Arg<Expression<Func<StudentHousingSelection, bool>>>().Compile()).ToList());
        _selections.GetAsync(_selection.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_selection);
        var students = Substitute.For<IStudentInformationSystemStudentLookup>();
        students.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        students.FindByExternalStudentIdAsync("123", Arg.Any<CancellationToken>()).Returns(
            new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements, "123", "test", "student@example.test", "Test", null, "Student"));
        _plans.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _plans.GetContextAsync("123", Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(
            new StudentMealPlanContext("Commuter", [], [new(269, "Spring 2027", new(2027, 1, 11), new(2027, 4, 30))], 269, [], null));
        _removal.GetAssignmentsAsync("123", Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentMealPlanAssignment>());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(_tenantId);
        var user = Substitute.For<ICurrentUser>(); user.Id.Returns(Guid.NewGuid());
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        _service = new(new[] { students }, new[] { _plans }, NullLogger<AdminMealPlanAppService>.Instance, _removal, _selections, MealPlanRemovalTestDouble.Create())
            { LazyServiceProvider = lazy };
    }

    private ClearNoMealPlanSelectionInput Input() => new()
    {
        ExternalStudentId = "123", TermCalendarId = 269, SelectionId = _selection.Id,
        ConcurrencyStamp = _selection.ConcurrencyStamp, Confirmed = true, Reason = "Student requested a different plan."
    };

    [Fact]
    public async Task Recorded_None_is_visible_and_clearable_without_any_Elements_assignment_or_write()
    {
        var context = await _service.GetContextAsync("123", 269);
        Assert.Equal(_selection.Id, Assert.Single(context.NoMealPlanSelections).SelectionId);
        Assert.Empty(context.Assignments);
        Assert.False(context.CanRemove); // Disabling external writes does not disable a local reset.
        await _service.ClearNoMealPlanSelectionAsync(Input());
        await _selections.Received(1).DeleteAsync(_selection, true, Arg.Any<CancellationToken>());
        await _plans.DidNotReceiveWithAnyArgs().AssignAsync(default!, default, default);
        await _plans.DidNotReceiveWithAnyArgs().UpdateHousingStatusAsync(default!, default, default, default);
        await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default, default, default, default);
    }

    [Theory]
    [InlineData(false, "Reason")]
    [InlineData(true, "")]
    public async Task Confirmation_and_reason_are_required(bool confirmed, string reason)
    {
        var input = Input(); input.Confirmed = confirmed; input.Reason = reason;
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(input));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
    }

    [Fact]
    public async Task A_stale_confirmation_or_other_term_cannot_clear_the_choice()
    {
        var input = Input(); input.ConcurrencyStamp = "stale";
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(input));
        input = Input(); input.TermCalendarId = 268;
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(input));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
    }

    [Fact]
    public async Task A_real_plan_is_never_cleared_by_the_local_None_action()
    {
        _selection.Update(HousingChoice.Commuter, 20, "20 meals");
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(Input()));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
        await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default, default, default, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ownership_is_rechecked_after_loading_the_reviewed_record(bool otherTenant)
    {
        var changed = new StudentHousingSelection(_selection.Id, otherTenant ? Guid.NewGuid() : _tenantId,
            Guid.NewGuid(), otherTenant ? "123" : "456", "January 2027", new(2027, 1, 11), new(2027, 4, 30),
            HousingChoice.Commuter, null, "None");
        // Match the preview's stamp so the ownership guard, not only optimistic concurrency, must reject it.
        changed.ConcurrencyStamp = _selection.ConcurrencyStamp;
        _selections.GetAsync(_selection.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(changed);
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(Input()));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
    }

    [Fact]
    public async Task A_real_Elements_assignment_blocks_resetting_None_into_a_second_selection()
    {
        _removal.GetAssignmentsAsync("123", Arg.Any<CancellationToken>()).Returns(
            new[] { new StudentMealPlanAssignment(10, 20, 269, "20 meals", 315m) });
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ClearNoMealPlanSelectionAsync(Input()));
        await _selections.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!, default, default);
        await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task Other_students_terms_and_real_plans_are_not_listed_as_local_None()
    {
        _rows.Add(new(Guid.NewGuid(), _tenantId, Guid.NewGuid(), "456", "January 2027", new(2027, 1, 11), new(2027, 4, 30), HousingChoice.Commuter, null, "None"));
        _rows.Add(new(Guid.NewGuid(), _tenantId, Guid.NewGuid(), "123", "August 2027", new(2027, 8, 20), new(2027, 12, 15), HousingChoice.Commuter, null, "None"));
        _rows.Add(new(Guid.NewGuid(), _tenantId, Guid.NewGuid(), "123", "January 2027", new(2027, 1, 11), new(2027, 4, 30), HousingChoice.Commuter, 20, "20 meals"));
        Assert.Equal(_selection.Id, Assert.Single((await _service.GetContextAsync("123", 269)).NoMealPlanSelections).SelectionId);
    }

    [Fact]
    public void Clearing_a_choice_is_audited_and_requires_the_existing_removal_permission()
    {
        var method = typeof(AdminMealPlanAppService).GetMethod(nameof(AdminMealPlanAppService.ClearNoMealPlanSelectionAsync))!;
        Assert.Contains(method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), x => x.Policy == CampusFlowPermissions.Admin.RemoveStudentMealPlan);
        Assert.Single(method.GetCustomAttributes(typeof(AuditedAttribute), false));
    }
}
