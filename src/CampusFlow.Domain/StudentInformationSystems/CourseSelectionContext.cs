namespace CampusFlow.StudentInformationSystems;

public sealed record CourseSelectionContext(
    StudentInformationSystemProvider Provider,
    string ExternalStudentId,
    string ExternalTermId,
    string TermName,
    string AttendanceType,
    decimal MaximumAllowedCredits);
