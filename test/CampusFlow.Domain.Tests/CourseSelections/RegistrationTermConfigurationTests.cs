using System;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class RegistrationTermConfigurationTests
{
    [Fact]
    public void A_term_is_open_only_when_enabled_and_inside_its_own_window()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        term.IsOpen(new DateTime(2026, 2, 28)).ShouldBeFalse();
        term.IsOpen(new DateTime(2026, 4, 1)).ShouldBeTrue();
        term.IsOpen(new DateTime(2026, 6, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Separate_terms_can_have_overlapping_open_windows()
    {
        var summer = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        var fall = Create(new DateTime(2026, 4, 1), new DateTime(2026, 8, 31), true);
        summer.IsOpen(new DateTime(2026, 4, 15)).ShouldBeTrue();
        fall.IsOpen(new DateTime(2026, 4, 15)).ShouldBeTrue();
    }

    [Fact]
    public void Disabled_term_remains_closed_during_its_window() =>
        Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), false)
            .IsOpen(new DateTime(2026, 4, 1)).ShouldBeFalse();

    [Fact]
    public void Specific_students_can_select_outside_the_general_window()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        term.ConfigureStudentAccess(["20000041293", "20000041293", "20000049999"]);
        var afterWindow = new DateTime(2026, 6, 1);

        term.GetStudentAccessIds().Count.ShouldBe(2);
        term.IsOpen(afterWindow).ShouldBeFalse();
        term.IsOpenForStudent(new DateTime(2026, 4, 1), "20000040000").ShouldBeTrue();
        term.IsOpenForStudent(afterWindow, "20000041293").ShouldBeTrue();
        term.IsOpenForStudent(afterWindow, "20000049999").ShouldBeTrue();
        term.IsOpenForStudent(afterWindow, "20000040000").ShouldBeFalse();
        term.CreatePolicy().GetEligibilityRules().IsOpen(afterWindow).ShouldBeFalse();
        term.CreatePolicy(allowOutsideWindow: true).GetEligibilityRules().IsOpen(afterWindow).ShouldBeTrue();
    }

    [Fact]
    public void Disabling_the_term_also_closes_student_specific_access()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), false);
        term.ConfigureStudentAccess(["20000041293"]);

        term.IsOpenForStudent(new DateTime(2026, 6, 1), "20000041293").ShouldBeFalse();
    }

    [Fact]
    public void Removing_a_student_id_revokes_its_exception()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        term.ConfigureStudentAccess(["20000041293"]);
        term.ConfigureStudentAccess([]);

        term.IsOpenForStudent(new DateTime(2026, 6, 1), "20000041293").ShouldBeFalse();
    }

    [Fact]
    public void Student_specific_access_does_not_match_an_elements_student_uid()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        term.ConfigureStudentAccess(["20000041293"]);

        term.IsOpenForStudent(new DateTime(2026, 6, 1), "41293").ShouldBeFalse();
    }

    [Fact]
    public void Dashboard_default_must_also_be_student_selectable()
    {
        var term = Create(new DateTime(2026, 3, 1), new DateTime(2026, 5, 31), true);
        term.ConfigureDashboard(false, true);
        term.IsStudentSelectable.ShouldBeFalse();
        term.IsDashboardDefault.ShouldBeFalse();
        term.ConfigureDashboard(true, true);
        term.IsDashboardDefault.ShouldBeTrue();
    }

    private static RegistrationTermConfiguration Create(DateTime opens, DateTime closes, bool enabled) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), "B26S", "Summer 2026",
            opens, closes, enabled, true, true, "[]");
}
