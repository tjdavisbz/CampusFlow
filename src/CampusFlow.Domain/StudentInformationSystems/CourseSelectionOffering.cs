using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record CourseSelectionOffering(
    StudentInformationSystemProvider Provider,
    string ExternalOfferingId,
    string ExternalMasterCourseId,
    string ExternalTermId,
    string TermName,
    string Department,
    string CourseCode,
    string CourseType,
    string Section,
    string CourseName,
    decimal Credits,
    string CourseAttendanceType,
    int MaximumEnrollment,
    int CurrentEnrollment,
    int TemporaryEnrollment,
    string? InstructorName,
    string? MeetingDays,
    TimeSpan? StartTime,
    TimeSpan? EndTime)
{
    public int SeatsRemaining => Math.Max(0, MaximumEnrollment - CurrentEnrollment - TemporaryEnrollment);
}
