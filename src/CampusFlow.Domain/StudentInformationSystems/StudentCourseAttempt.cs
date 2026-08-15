namespace CampusFlow.StudentInformationSystems;

public sealed record StudentCourseAttempt(
    string Department,
    string CourseCode,
    string CourseType,
    string Grade,
    bool WasWithdrawn);
