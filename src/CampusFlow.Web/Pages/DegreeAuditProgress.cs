using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.Web.Pages;

public sealed record DegreeAuditProgress(
    decimal CreditsRequired,
    decimal CreditsCompleted,
    decimal InProgressCredits,
    decimal ProjectedCreditsCompleted,
    decimal CompletionPercent,
    decimal ProjectedCompletionPercent)
{
    public static DegreeAuditProgress From(
        StudentDegreeAuditDetail? audit,
        decimal? selectedCourseCredits = null)
    {
        if (audit is null || audit.Summary.CreditsRequired <= 0)
            return new(0m, 0m, 0m, 0m, 0m, 0m);

        var inProgressCredits = selectedCourseCredits ?? audit.Courses
                .Where(course => IsInProgress(course.CourseStatus) && course.Credits > 0)
                .GroupBy(course => new
                {
                    Course = string.IsNullOrWhiteSpace(course.CourseCode)
                        ? course.MatchedCourseCode.Trim().ToUpperInvariant()
                        : course.CourseCode.Trim().ToUpperInvariant(),
                    Term = course.Term.Trim().ToUpperInvariant()
                })
                .Sum(course => course.Max(item => item.Credits));
        inProgressCredits = Math.Max(0m, inProgressCredits);
        var completed = Math.Min(audit.Summary.CreditsRequired, audit.Summary.CreditsCompleted);
        var projected = Math.Min(audit.Summary.CreditsRequired, completed + inProgressCredits);

        return new(
            audit.Summary.CreditsRequired,
            completed,
            inProgressCredits,
            projected,
            completed / audit.Summary.CreditsRequired * 100m,
            projected / audit.Summary.CreditsRequired * 100m);
    }

    private static bool IsInProgress(string status) =>
        status.Trim().ToUpperInvariant() is "INP" or "IN PROGRESS";
}
