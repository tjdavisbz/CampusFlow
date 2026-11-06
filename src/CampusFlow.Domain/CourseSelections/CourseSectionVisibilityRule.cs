using System;
using System.Collections.Generic;
using System.Linq;
namespace CampusFlow.CourseSelections;

public sealed record CourseSectionVisibilityRule(string StudentAttendanceType, IReadOnlyList<string> SectionCodes)
{
    public CourseSectionVisibilityRule Normalize()
    {
        if (string.IsNullOrWhiteSpace(StudentAttendanceType) || StudentAttendanceType.Trim().Length > 100 || SectionCodes is null)
            throw new ArgumentException("Choose a student attendance type and at least one section code.");
        var codes = SectionCodes.Select(x => x?.Trim() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        if (codes.Length is < 1 or > 500 || codes.Any(x => x.Length is < 1 or > 30 || !x.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Use exact section codes, separated by commas, spaces or new lines.");
        return new(StudentAttendanceType.Trim(), codes);
    }
}
