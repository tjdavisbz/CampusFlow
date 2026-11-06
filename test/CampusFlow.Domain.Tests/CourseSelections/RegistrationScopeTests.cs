using System;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Data;
using Xunit;
namespace CampusFlow.CourseSelections;

public class RegistrationScopeTests
{
    private static AdvisorAssignment Assignment(string attendance = "Residential") =>
        new(Guid.NewGuid(), null, attendance, "1", "scheduler@example.test", "Scheduler", DateTime.UtcNow);
    private static StudentPrimaryProgram Primary(int major = 10, int? degree = 20, int? program = 30) =>
        new(new("100", "269"), major, "Primary major", degree, "Degree", program, "Program");

    [Fact]
    public void Existing_assignment_without_restrictions_preserves_attendance_access()
    {
        var assignment = Assignment();
        Assert.True(assignment.IncludesStudent("Residential", false));
        Assert.False(assignment.IncludesStudent("Distance", false, Primary()));
    }

    [Fact]
    public void All_configured_dimensions_must_match_the_primary_program()
    {
        var assignment = Assignment();
        assignment.ConfigurePrimaryProgramScope(new([10], [20], [30]));
        Assert.True(assignment.IncludesStudent("Residential", false, Primary()));
        Assert.False(assignment.IncludesStudent("Residential", false, Primary(11)));
        Assert.False(assignment.IncludesStudent("Residential", false, Primary(degree: 21)));
        Assert.False(assignment.IncludesStudent("Residential", false, Primary(program: 31)));
        Assert.False(assignment.IncludesStudent("Residential", false, null));
    }

    [Fact]
    public void Restricted_global_assignment_does_not_bypass_program_or_graduation_rules()
    {
        var assignment = Assignment(AdvisorAssignment.GlobalAttendanceType);
        assignment.ConfigurePrimaryProgramScope(new([10], [], []));
        assignment.SetIncludeGraduatingStudents(false);
        Assert.True(assignment.IncludesStudent("Distance", false, Primary()));
        Assert.False(assignment.IncludesStudent("Distance", false, Primary(11)));
        Assert.False(assignment.IncludesStudent("Distance", true, Primary()));
    }

    [Fact]
    public void Missing_degree_or_program_does_not_match_a_restricted_dimension()
    {
        var scope = new PrimaryProgramScope([], [20], [30]);
        Assert.False(scope.Includes(Primary(degree: null)));
        Assert.False(scope.Includes(Primary(program: null)));
    }

    [Fact]
    public void Corrupt_scope_does_not_revert_to_global_access()
    {
        var assignment = Assignment();
        assignment.SetProperty(AdvisorAssignment.PrimaryProgramScopeProperty, "broken");
        Assert.Throws<InvalidOperationException>(() => assignment.IncludesStudent("Residential", false, Primary()));
    }

    private static RegistrationTermConfiguration Term() => new(Guid.NewGuid(), null, "269", "B27C", "Spring 2027",
        DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), true, true, true, "[]");
    private static CourseSelectionContext Context(string attendance = "Residential") =>
        new(StudentInformationSystemProvider.ThesisElements, "100", "269", "Spring 2027", attendance, 18, ApplicantStatus: "Matriculated");
    private static CourseSelectionOffering Offering(string section = "100", string attendance = "Residential", int enrollment = 0) =>
        new(StudentInformationSystemProvider.ThesisElements, "1", "2", "269", "Spring 2027", "TEST", "101", "", section, "Sample", 3,
            attendance, 20, enrollment, 0, null, "M", TimeSpan.FromHours(9), TimeSpan.FromHours(10));

    [Fact]
    public void Exact_sections_narrow_only_the_configured_student_group()
    {
        var term = Term();
        term.ConfigureSectionVisibility([new("Residential", ["100", "101"])]);
        var policy = term.CreatePolicy();
        Assert.True(policy.CanSelect(Context(), Offering()));
        Assert.False(policy.CanSelect(Context(), Offering("102")));
        Assert.True(policy.CanSelect(Context("Distance"), Offering("102", "Distance")));
        Assert.False(policy.CanSelect(Context(), Offering("1000")));
    }

    [Fact]
    public void Section_rules_never_bypass_attendance_capacity_or_term()
    {
        var term = Term();
        term.ConfigureSectionVisibility([new("Residential", ["100"])]);
        var policy = term.CreatePolicy();
        Assert.False(policy.CanSelect(Context(), Offering(attendance: "Distance")));
        Assert.False(policy.CanSelect(Context(), Offering(enrollment: 20)));
        Assert.False(policy.CanSelect(Context(), Offering() with { ExternalTermId = "268" }));
    }

    [Fact]
    public void Empty_or_duplicate_restrictions_are_rejected_and_removing_rules_restores_prior_access()
    {
        var term = Term();
        Assert.Throws<ArgumentException>(() => term.ConfigureSectionVisibility([new("Residential", [])]));
        Assert.Throws<ArgumentException>(() => term.ConfigureSectionVisibility([new("Residential", ["100"]), new("residential", ["101"])]));
        term.ConfigureSectionVisibility([new("Residential", ["100"])]);
        term.ConfigureSectionVisibility([]);
        Assert.True(term.CreatePolicy().CanSelect(Context(), Offering("102")));
    }

    [Fact]
    public void Corrupt_term_restrictions_fail_closed()
    {
        var term = Term();
        term.SetProperty(RegistrationTermConfiguration.SectionVisibilityProperty, "not json");
        Assert.Throws<InvalidOperationException>(() => term.CreatePolicy());
    }
}
