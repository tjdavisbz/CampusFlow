namespace CampusFlow.CourseSelections;

public sealed record CourseSelectionAttendanceTypeMapping(
    string TermPattern,
    string StudentAttendanceType,
    string CourseAttendanceType);
