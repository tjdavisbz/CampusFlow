using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class CourseReviewSubmission : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalTermId { get; private set; } = null!;
    public Guid SubmittedByUserId { get; private set; }
    public string AdvisorEmail { get; private set; } = null!;
    public string? OverallComment { get; private set; }
    public string DecisionsSnapshotJson { get; private set; } = "[]";
    public DateTime SubmittedAt { get; private set; }
    public ReviewEmailStatus EmailStatus { get; private set; }
    public int EmailAttemptCount { get; private set; }
    public DateTime? LastEmailAttemptAt { get; private set; }
    public string? LastEmailError { get; private set; }

    protected CourseReviewSubmission() { }

    public CourseReviewSubmission(Guid id, Guid? tenantId, Guid studentProfileId,
        string externalTermId, Guid submittedByUserId, string advisorEmail,
        string? overallComment, string decisionsSnapshotJson, DateTime submittedAt,
        ReviewEmailStatus emailStatus) : base(id)
    {
        TenantId = tenantId;
        StudentProfileId = studentProfileId;
        ExternalTermId = externalTermId;
        SubmittedByUserId = submittedByUserId;
        AdvisorEmail = advisorEmail;
        OverallComment = overallComment;
        DecisionsSnapshotJson = decisionsSnapshotJson;
        SubmittedAt = submittedAt;
        EmailStatus = emailStatus;
    }
}
