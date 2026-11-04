using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record CourseSelectionContext(
    StudentInformationSystemProvider Provider,
    string ExternalStudentId,
    string ExternalTermId,
    string TermName,
    string AttendanceType,
    decimal MaximumAllowedCredits,
    string CollegeLevel = "",
    string ApplicantStatus = "")
{
    public bool IsMatriculated => string.Equals(ApplicantStatus.Trim(), "Matriculated",
        StringComparison.OrdinalIgnoreCase);
}
