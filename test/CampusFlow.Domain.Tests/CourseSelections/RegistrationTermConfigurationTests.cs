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
