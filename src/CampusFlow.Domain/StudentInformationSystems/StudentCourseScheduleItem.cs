using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentCourseScheduleItem(
    StudentInformationSystemProvider Provider,
    string ExternalAcademicId,
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
    string? FinalLetterGrade);
