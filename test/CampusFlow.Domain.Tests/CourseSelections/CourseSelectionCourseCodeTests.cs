using CampusFlow.CourseSelections;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class CourseSelectionCourseCodeTests
{
    [Theory]
    [InlineData("COU", "3311", "", "COU3311")]
    [InlineData("PSY", "3311", "", "PSY3311")]
    [InlineData(" ENGL ", " 1301 ", " A ", "ENGL1301-A")]
    public void Includes_department_and_optional_course_type(
        string department, string number, string courseType, string expected)
    {
        CourseSelectionCourseCode.Format(department, number, courseType).ShouldBe(expected);
    }
}
