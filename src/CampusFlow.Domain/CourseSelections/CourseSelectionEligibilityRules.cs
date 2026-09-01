using System;

namespace CampusFlow.CourseSelections;

public sealed record CourseSelectionEligibilityRules(
    bool RequireStudentStatus = true,
    bool RequireTermDisplayedInPortal = true,
    bool RequireCourseDisplayedInPortal = true,
    bool ExcludeFullSections = true,
    DateTime? RegistrationOpensAt = null,
    DateTime? RegistrationClosesAt = null)
{
    public bool IsOpen(DateTime at) =>
        (RegistrationOpensAt is null || RegistrationOpensAt <= at) &&
        (RegistrationClosesAt is null || RegistrationClosesAt >= at);
}
