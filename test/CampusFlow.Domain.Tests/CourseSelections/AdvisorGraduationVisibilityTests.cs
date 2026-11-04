using System;
using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class AdvisorGraduationVisibilityTests
{
    [Theory]
    [InlineData("Matriculated", true)]
    [InlineData(" matriculated ", true)]
    [InlineData("Accepted", false)]
    [InlineData("Pre-Accept", false)]
    [InlineData("Conditionally Matriculated Hold", false)]
    [InlineData("Matriculated Dual-Credit", false)]
    [InlineData("", false)]
    public void Only_exact_matriculated_status_can_select_courses(string applicantStatus, bool expected)
    {
        var context = new CourseSelectionContext(StudentInformationSystemProvider.ThesisElements,
            "student", "term", "Spring 2027", "Undergraduate", 18m,
            ApplicantStatus: applicantStatus);

        context.IsMatriculated.ShouldBe(expected);
    }

    [Fact]
    public void Scheduler_can_exclude_graduating_students_without_losing_other_students()
    {
        var assignment = CreateAssignment("Undergraduate", false);

        assignment.IncludesStudent("Undergraduate", false).ShouldBeTrue();
        assignment.IncludesStudent("Undergraduate", true).ShouldBeFalse();
        assignment.IncludesStudent("Graduate", false).ShouldBeFalse();

        assignment.SetIncludeGraduatingStudents(true);
        assignment.IncludesStudent("Undergraduate", true).ShouldBeTrue();
    }

    [Fact]
    public void Global_scheduler_is_subject_to_graduation_setting()
    {
        var assignment = CreateAssignment(AdvisorAssignment.GlobalAttendanceType, false);

        assignment.IncludesStudent("Undergraduate", false).ShouldBeTrue();
        assignment.IncludesStudent("Graduate", true).ShouldBeFalse();
    }

    [Fact]
    public void Existing_assignments_include_graduating_students_by_default()
    {
        CreateAssignment("Undergraduate").IncludesStudent("Undergraduate", true).ShouldBeTrue();
    }

    [Fact]
    public void Manually_removed_pending_course_disappears_but_rejected_course_remains_in_email()
    {
        var pending = CreateReview("101", true);
        var approved = CreateReview("102", false);
        var rejected = CreateReview("103", true);
        rejected.BeginRejection(Guid.NewGuid(), DateTime.UtcNow, null);
        rejected.CompleteRejection();

        var visible = CourseReviewReconciliation.KeepActivePending([pending, approved, rejected],
            new HashSet<string>());

        visible.ShouldNotContain(pending);
        visible.ShouldContain(approved);
        visible.ShouldContain(rejected);
    }

    private static AdvisorAssignment CreateAssignment(string attendanceType, bool includeGraduating = true) =>
        new(Guid.NewGuid(), null, attendanceType, "faculty", "faculty@nelson.edu",
            "Test Faculty", DateTime.UtcNow, includeGraduatingStudents: includeGraduating);

    private static CourseReview CreateReview(string registrationId, bool needsReview) =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "student", "term", "offering",
            registrationId, "Undergraduate", "{}", null, null, needsReview);
}
