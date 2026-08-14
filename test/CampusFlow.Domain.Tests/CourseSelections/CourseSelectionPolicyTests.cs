using System;
using System.Text.Json;
using CampusFlow.StudentInformationSystems;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class CourseSelectionPolicyTests
{
    [Theory]
    [InlineData("Summer 2026", "Graduate", "Distance Graduate")]
    [InlineData("Summer 2026", "Residential Undergraduate", "Distance Education")]
    [InlineData("Fall 2026", "Residential Undergraduate", "Residential Undergraduate")]
    public void Attendance_mapping_is_term_aware(string term, string attendanceType, string expected)
    {
        CreatePolicy(true).ResolveCourseAttendanceType(term, attendanceType).ShouldBe(expected);
    }

    [Fact]
    public void Full_section_is_blocked_when_capacity_is_enforced()
    {
        var policy = CreatePolicy(true);
        var context = CreateContext();
        var offering = CreateOffering(maximum: 20, current: 20);

        policy.CanSelect(context, offering).ShouldBeFalse();
    }

    [Fact]
    public void Full_section_can_be_shown_when_capacity_is_not_enforced()
    {
        var policy = CreatePolicy(false);
        var context = CreateContext();
        var offering = CreateOffering(maximum: 20, current: 20);

        policy.CanSelect(context, offering).ShouldBeTrue();
    }

    private static CourseSelectionPolicy CreatePolicy(bool enforceCapacity)
    {
        CourseSelectionAttendanceTypeMapping[] mappings =
        [
            new("Summer*", "Graduate", "Distance Graduate"),
            new("Summer*", "Residential Undergraduate", "Distance Education")
        ];
        return new CourseSelectionPolicy(Guid.NewGuid(), Guid.NewGuid(), "Test", 1,
            new DateTime(2026, 1, 1), true, true, enforceCapacity,
            JsonSerializer.Serialize(mappings), "{}");
    }

    private static CourseSelectionContext CreateContext() => new(
        StudentInformationSystemProvider.ThesisElements, "13465", "300", "Fall 2026",
        "Residential Undergraduate", 18m);

    private static CourseSelectionOffering CreateOffering(int maximum, int current) => new(
        StudentInformationSystemProvider.ThesisElements, "10", "20", "300", "Fall 2026",
        "ENG", "101", "LEC", "01", "English", 3m, "Residential Undergraduate",
        maximum, current, 0, null, null, null, null);
}
