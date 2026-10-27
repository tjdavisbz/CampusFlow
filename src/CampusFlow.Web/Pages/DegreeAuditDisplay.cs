using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.Web.Pages;

public static class DegreeAuditDisplay
{
    public static bool IsElective(StudentDegreeAuditCourse course) =>
        course.RequirementName.Contains("electiv", StringComparison.OrdinalIgnoreCase) ||
        course.GroupName.Contains("electiv", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<StudentDegreeAuditCourse> CoursesForGroup(
        IEnumerable<StudentDegreeAuditCourse> groupCourses,
        IReadOnlyCollection<StudentDegreeAuditCourse> auditCourses)
    {
        var courses = groupCourses.ToArray();
        if (courses.Length == 0 || !IsElective(courses[0]))
        {
            return courses;
        }

        // Elective groups list every course that could qualify. Show only actual
        // coursework, and never show the same matched class twice when it is
        // already applied to a prescribed (non-elective) requirement.
        var prescribedMatches = auditCourses
            .Where(course => !IsElective(course) && IsApplied(course) && course.MatchedAcademicId > 0)
            .Select(course => course.MatchedAcademicId)
            .ToHashSet();

        return courses
            .Where(course => IsApplied(course) &&
                             (course.MatchedAcademicId <= 0 ||
                              !prescribedMatches.Contains(course.MatchedAcademicId)))
            .ToArray();
    }

    private static bool IsApplied(StudentDegreeAuditCourse course) =>
        course.CourseStatus.Trim().ToUpperInvariant() is
            "C" or "MC" or "TC" or "COMPLETED" or "INP" or "IN PROGRESS";
}
