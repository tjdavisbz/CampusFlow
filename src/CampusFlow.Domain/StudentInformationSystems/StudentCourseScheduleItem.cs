using System;
using System.Collections.Generic;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentCourseScheduleItem(
    StudentInformationSystemProvider Provider,
    string ExternalAcademicId,
    string ExternalCourseOfferingId,
    string ExternalTermId,
    string TermCode,
    string TermName,
    string Department,
    string CourseNumber,
    string CourseType,
    string Section,
    string CourseName,
    decimal Credits,
    string RegistrationStatus,
    DateTime? StartDate,
    DateTime? EndDate,
    string Instructor,
    string? MeetingDays,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    string? Room,
    decimal? MidtermGrade,
    decimal? FinalNumericGrade,
    string? FinalLetterGrade)
{
    public bool IsApprovedByScheduler { get; init; }
    public string DisplayRegistrationStatus => IsApprovedByScheduler && string.Equals(RegistrationStatus, "Unofficial", System.StringComparison.OrdinalIgnoreCase) ? "Approved" : RegistrationStatus;
    public IReadOnlyList<CourseMeeting> Meetings { get; init; } = [];
}
