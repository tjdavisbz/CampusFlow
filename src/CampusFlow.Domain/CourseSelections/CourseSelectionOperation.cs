using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public enum CourseSelectionOperationStatus
{
    Pending,
    ExternalRegistrationCompleted,
    Completed,
    Failed,
    ReconciliationRequired
}

public class CourseSelectionOperation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string ExternalTermId { get; private set; } = null!;
    public string ExternalCourseOfferingId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string CourseSnapshotJson { get; private set; } = "{}";
    public CourseSelectionOperationStatus Status { get; private set; }
    public string? ExternalCourseRegistrationId { get; private set; }
    public Guid? CourseReviewId { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    protected CourseSelectionOperation() { }

    public CourseSelectionOperation(Guid id, Guid? tenantId, Guid studentProfileId,
        string externalStudentId, string externalTermId, string externalCourseOfferingId,
        string idempotencyKey, string courseSnapshotJson) : base(id)
    {
        TenantId = tenantId;
        StudentProfileId = studentProfileId;
        ExternalStudentId = externalStudentId;
        ExternalTermId = externalTermId;
        ExternalCourseOfferingId = externalCourseOfferingId;
        IdempotencyKey = idempotencyKey;
        CourseSnapshotJson = courseSnapshotJson;
        Status = CourseSelectionOperationStatus.Pending;
    }

    public void RecordAttempt(DateTime attemptedAt)
    {
        AttemptCount++;
        LastAttemptAt = attemptedAt;
        LastError = null;
    }

    public void RecordExternalRegistration(string externalRegistrationId)
    {
        ExternalCourseRegistrationId = externalRegistrationId;
        Status = CourseSelectionOperationStatus.ExternalRegistrationCompleted;
    }

    public void Complete(Guid courseReviewId)
    {
        CourseReviewId = courseReviewId;
        Status = CourseSelectionOperationStatus.Completed;
        LastError = null;
    }

    public void Fail(string error, bool externalRegistrationMayHaveCompleted = false)
    {
        LastError = error;
        Status = ExternalCourseRegistrationId is null && !externalRegistrationMayHaveCompleted
            ? CourseSelectionOperationStatus.Failed
            : CourseSelectionOperationStatus.ReconciliationRequired;
    }
}
