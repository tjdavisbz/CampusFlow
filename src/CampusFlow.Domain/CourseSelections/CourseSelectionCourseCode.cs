namespace CampusFlow.CourseSelections;

public static class CourseSelectionCourseCode
{
    public static string Format(string department, string number, string courseType)
    {
        var code = department.Trim() + number.Trim();
        return string.IsNullOrWhiteSpace(courseType) ? code : $"{code}-{courseType.Trim()}";
    }
}
