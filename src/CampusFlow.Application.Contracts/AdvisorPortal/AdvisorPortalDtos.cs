using System;
using System.Collections.Generic;

namespace CampusFlow.AdvisorPortal;

public sealed class AdvisorQueueItemDto
{
    public Guid StudentProfileId { get; set; }
    public string ExternalTermId { get; set; } = null!;
    public string StudentName { get; set; } = null!;
    public string StudentId { get; set; } = null!;
    public string AttendanceType { get; set; } = null!;
    public bool? IntendsToGraduate { get; set; }
    public int PendingCourseCount { get; set; }
    public DateTime WaitingSince { get; set; }
}

public sealed class AdvisorCourseReviewDto
{
    public Guid ReviewId { get; set; }
    public string CourseCode { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public decimal Credits { get; set; }
    public string? InstructorName { get; set; }
    public string? MeetingDays { get; set; }
    public string Status { get; set; } = null!;
    public string? AdvisorComment { get; set; }
    public string? LastRemovalError { get; set; }
}

public sealed class AdvisorStudentReviewDto
{
    public Guid StudentProfileId { get; set; }
    public string ExternalTermId { get; set; } = null!;
    public string StudentName { get; set; } = null!;
    public string StudentId { get; set; } = null!;
    public string StudentEmail { get; set; } = null!;
    public string AttendanceType { get; set; } = null!;
    public bool? IntendsToGraduate { get; set; }
    public List<AdvisorCourseReviewDto> Courses { get; set; } = [];
    public List<AdvisorScheduledCourseDto> ExistingCourses { get; set; } = [];
}

public sealed class AdvisorScheduledCourseDto
{
    public string CourseCode { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public decimal Credits { get; set; }
    public string Status { get; set; } = null!;
}

public sealed class AdvisorCourseDecisionInput
{
    public Guid ReviewId { get; set; }
    public string? Decision { get; set; }
    public string? Comment { get; set; }
}

public sealed class SubmitAdvisorReviewInput
{
    public Guid StudentProfileId { get; set; }
    public string ExternalTermId { get; set; } = null!;
    public string? OverallComment { get; set; }
    public List<AdvisorCourseDecisionInput> Courses { get; set; } = [];
}

public sealed class AdvisorReviewEmailDto
{
    public Guid? SubmissionId { get; set; }
    public string EmailStatus { get; set; } = "Pending";
    public string StudentName { get; set; } = null!;
    public string StudentEmail { get; set; } = null!;
    public string ExternalTermId { get; set; } = null!;
    public string TermName { get; set; } = null!;
    public string? OverallComment { get; set; }
    public List<AdvisorEmailCourseDto> Courses { get; set; } = [];
}

public sealed class AdvisorEmailCourseDto
{
    public string CourseCode { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? AdvisorComment { get; set; }
}
