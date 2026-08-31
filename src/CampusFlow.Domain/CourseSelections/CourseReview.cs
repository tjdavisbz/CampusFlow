using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class CourseReview : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string ExternalTermId { get; private set; } = null!;
    public string ExternalCourseOfferingId { get; private set; } = null!;
    public string ExternalCourseRegistrationId { get; private set; } = null!;
    public string AttendanceType { get; private set; } = null!;
    public string CourseSnapshotJson { get; private set; } = "{}";
    public string? ExternalAdvisorId { get; private set; }
    public string? AdvisorEmail { get; private set; }
    public CourseReviewStatus Status { get; private set; }
    public bool NeedsReview { get; private set; }
    public string? AdvisorComment { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public ExternalCourseRemovalStatus RemovalStatus { get; private set; }
    public int RemovalAttemptCount { get; private set; }
    public DateTime? LastRemovalAttemptAt { get; private set; }
    public string? LastRemovalError { get; private set; }

    protected CourseReview() { }

    public CourseReview(Guid id, Guid? tenantId, Guid studentProfileId,
        string externalStudentId, string externalTermId, string externalCourseOfferingId,
        string externalCourseRegistrationId, string attendanceType, string courseSnapshotJson,
        string? externalAdvisorId, string? advisorEmail, bool needsReview) : base(id)
    {
        TenantId = tenantId;
        StudentProfileId = studentProfileId;
        ExternalStudentId = externalStudentId;
        ExternalTermId = externalTermId;
        ExternalCourseOfferingId = externalCourseOfferingId;
        ExternalCourseRegistrationId = externalCourseRegistrationId;
        AttendanceType = attendanceType;
        CourseSnapshotJson = courseSnapshotJson;
        ExternalAdvisorId = externalAdvisorId;
        AdvisorEmail = advisorEmail;
        NeedsReview = needsReview;
        Status = needsReview ? CourseReviewStatus.Pending : CourseReviewStatus.Approved;
        RemovalStatus = ExternalCourseRemovalStatus.NotRequired;
    }

    public void RecordComment(string? comment) => AdvisorComment = comment;

    public void ReassignAdvisor(string? externalAdvisorId, string? advisorEmail)
    {
        if (!NeedsReview) return;
        ExternalAdvisorId = externalAdvisorId;
        AdvisorEmail = advisorEmail;
    }

    public void Approve(Guid userId, DateTime decidedAt, string? comment)
    {
        AdvisorComment = comment;
        DecidedByUserId = userId;
        DecidedAt = decidedAt;
        Status = CourseReviewStatus.Approved;
        NeedsReview = false;
        RemovalStatus = ExternalCourseRemovalStatus.NotRequired;
    }

    public void BeginRejection(Guid userId, DateTime decidedAt, string? comment)
    {
        AdvisorComment = comment;
        DecidedByUserId = userId;
        DecidedAt = decidedAt;
        Status = CourseReviewStatus.RejectionPending;
        NeedsReview = true;
        RemovalStatus = ExternalCourseRemovalStatus.Pending;
    }

    public void CompleteRejection()
    {
        Status = CourseReviewStatus.Rejected;
        NeedsReview = false;
        RemovalStatus = ExternalCourseRemovalStatus.Completed;
        LastRemovalError = null;
    }

    public void RecordRemovalFailure(DateTime attemptedAt, string error)
    {
        RemovalAttemptCount++;
        LastRemovalAttemptAt = attemptedAt;
        LastRemovalError = error;
        RemovalStatus = ExternalCourseRemovalStatus.Failed;
        Status = CourseReviewStatus.Failed;
        NeedsReview = true;
    }

    public void BeginStudentRemoval(DateTime attemptedAt)
    {
        RemovalAttemptCount++;
        LastRemovalAttemptAt = attemptedAt;
        LastRemovalError = null;
        RemovalStatus = ExternalCourseRemovalStatus.Pending;
    }

    public void CompleteStudentRemoval()
    {
        Status = CourseReviewStatus.RemovedByStudent;
        NeedsReview = false;
        RemovalStatus = ExternalCourseRemovalStatus.Completed;
        LastRemovalError = null;
    }
}
