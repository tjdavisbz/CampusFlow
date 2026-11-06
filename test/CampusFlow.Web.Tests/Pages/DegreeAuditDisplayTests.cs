using System.Linq;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.Pages;

public class DegreeAuditDisplayTests
{
    [Fact]
    public void Electives_show_only_applied_courses_not_used_by_prescribed_requirements()
    {
        var candidate = Course("General Electives", "Electives - 9", "BIO1013", "R");
        var completed = Course("General Electives", "Electives - 9", "COM2113", "C", 101);
        var inProgress = Course("General Electives", "Electives - 9", "HIS3113", "InP", 102);
        var prescribed = Course("Major", "Required Courses", "ENG2113", "C", 103);
        var duplicatedPrescribed = Course("General Electives", "Electives - 9", "ENG2113", "C", 103);
        var all = new[] { candidate, completed, inProgress, prescribed, duplicatedPrescribed };

        var visible = DegreeAuditDisplay.CoursesForGroup(
            new[] { candidate, completed, inProgress, duplicatedPrescribed }, all);

        Assert.Equal(new[] { "COM2113", "HIS3113" }, visible.Select(x => x.CourseCode));
    }

    [Fact]
    public void Prescribed_groups_keep_remaining_courses()
    {
        var remaining = Course("Major", "Required Courses", "ENG2113", "R");

        var visible = DegreeAuditDisplay.CoursesForGroup(new[] { remaining }, new[] { remaining });

        Assert.Same(remaining, Assert.Single(visible));
    }

    [Fact]
    public void Elective_requirement_is_filtered_even_when_group_name_is_generic()
    {
        var remaining = Course("Elective Component", "Major - Open Choices", "HIS2113", "R");
        var transfer = Course("Elective Component", "Major - Open Choices", "SOC2113", "TC");

        var visible = DegreeAuditDisplay.CoursesForGroup(
            new[] { remaining, transfer }, new[] { remaining, transfer });

        Assert.Same(transfer, Assert.Single(visible));
    }

    [Fact]
    public void Repeated_instructor_rows_do_not_repeat_an_audit_course()
    {
        var course = Course("Major", "Required", "ENG2113", "InP", 100);
        Assert.Single(DegreeAuditDisplay.CoursesForGroup([course, course], [course, course]));
    }

    [Fact]
    public void Progress_counts_each_registration_once_across_requirements_but_keeps_separate_attempts()
    {
        var course = Course("Major", "Required", "ENG2113", "InP", 100);
        var summary = new StudentDegreeAuditSummary(269, 1, 1, "BA", "Example", "2027", 120, 30, 2, 3, "R", false);
        var audit = new StudentDegreeAuditDetail(summary, 3,
            [course, course with { RequirementName = "Other" }, course with { MatchedAcademicId = 101 }]);
        var progress = DegreeAuditProgress.From(audit);
        Assert.Equal(6, progress.InProgressCredits);
        Assert.Equal(36, progress.ProjectedCreditsCompleted);
        Assert.Equal(30, audit.Summary.CreditsCompleted);
    }

    private static StudentDegreeAuditCourse Course(
        string requirement, string group, string code, string status, int academicId = 0) =>
        new(requirement, 9, 3, 0, 0, "R", 1,
            group, 9, 3, 0, 0, "R", 1,
            code, code, status, "", 3, "", code, academicId);
}
