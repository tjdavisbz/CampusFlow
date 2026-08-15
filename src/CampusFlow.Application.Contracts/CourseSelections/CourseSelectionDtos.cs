using System;
using System.Collections.Generic;

namespace CampusFlow.CourseSelections;

public sealed class CourseSelectionOfferingDto
{
    public string ExternalOfferingId { get; set; } = null!;
    public string CourseCode { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public decimal Credits { get; set; }
    public string CourseAttendanceType { get; set; } = null!;
    public int SeatsRemaining { get; set; }
    public string? InstructorName { get; set; }
    public string? MeetingDays { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public bool CanSelect { get; set; }
    public bool FulfillsDegreeRequirement { get; set; }
    public string? DegreeRequirementName { get; set; }
    public bool WasPreviouslyTaken { get; set; }
    public string? PreviousGrade { get; set; }
    public string? PreviousAttemptOutcome { get; set; }
}

public sealed class CourseSelectionRegistrationDto
{
    public string ExternalRegistrationId { get; set; } = null!;
    public string ExternalOfferingId { get; set; } = null!;
    public string CourseCode { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public decimal Credits { get; set; }
    public string Status { get; set; } = null!;
    public bool NeedsReview { get; set; }
    public bool CanRemove { get; set; }
}

public sealed class CourseSelectionDto
{
    public string ExternalTermId { get; set; } = null!;
    public string TermName { get; set; } = null!;
    public string AttendanceType { get; set; } = null!;
    public decimal MaximumAllowedCredits { get; set; }
    public decimal SelectedCredits { get; set; }
    public bool DegreeAuditAvailable { get; set; }
    public List<CourseSelectionOfferingDto> Offerings { get; set; } = [];
    public List<CourseSelectionRegistrationDto> Registrations { get; set; } = [];
}

public sealed class CourseSelectionTermDto
{
    public string ExternalTermId { get; set; } = null!;
    public string TermName { get; set; } = null!;
}

public sealed class AddCourseSelectionInput
{
    public string ExternalTermId { get; set; } = null!;
    public string ExternalOfferingId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
}

public sealed class AddCourseSelectionResultDto
{
    public Guid OperationId { get; set; }
    public Guid? CourseReviewId { get; set; }
    public string? ExternalRegistrationId { get; set; }
    public string Status { get; set; } = null!;
}

public sealed class RemoveCourseSelectionInput
{
    public string ExternalTermId { get; set; } = null!;
    public string ExternalOfferingId { get; set; } = null!;
    public string ExternalRegistrationId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
}
