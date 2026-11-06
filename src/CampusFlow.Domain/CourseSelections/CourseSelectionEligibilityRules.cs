using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.CourseSelections;

public sealed record CourseSelectionEligibilityRules(
    bool RequireStudentStatus = true,
    bool RequireTermDisplayedInPortal = true,
    bool RequireCourseDisplayedInPortal = true,
    bool ExcludeFullSections = true,
    DateTime? RegistrationOpensAt = null,
    DateTime? RegistrationClosesAt = null,
    IReadOnlyList<CourseSectionVisibilityRule>? SectionVisibilityRules = null)
{
    public bool AllowsSection(string attendanceType, string section) => SectionVisibilityRules is null ||
        SectionVisibilityRules.Where(x => string.Equals(x.StudentAttendanceType, attendanceType.Trim(), StringComparison.OrdinalIgnoreCase))
            .All(x => x.SectionCodes.Contains(section.Trim(), StringComparer.OrdinalIgnoreCase));
    public bool IsOpen(DateTime at) =>
        (RegistrationOpensAt is null || RegistrationOpensAt <= at) &&
        (RegistrationClosesAt is null || RegistrationClosesAt >= at);
}
