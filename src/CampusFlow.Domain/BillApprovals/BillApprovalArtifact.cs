using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class BillApprovalArtifact : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BillApprovalId { get; private set; }
    public string? PdfFileName { get; private set; }
    public string? PdfSha256 { get; private set; }
    public string? PdfBlobName { get; private set; }
    public string? ElementsDocumentTrackingId { get; private set; }
    public BillArtifactOperationStatus PdfStatus { get; private set; }
    public BillArtifactOperationStatus DocumentUploadStatus { get; private set; }
    public BillArtifactOperationStatus StudentEmailStatus { get; private set; }
    public BillArtifactOperationStatus BillingEmailStatus { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    protected BillApprovalArtifact() { }
    public BillApprovalArtifact(Guid id, Guid? tenantId, Guid billApprovalId) : base(id)
    {
        TenantId = tenantId;
        BillApprovalId = billApprovalId;
        PdfStatus = BillArtifactOperationStatus.Pending;
        DocumentUploadStatus = BillArtifactOperationStatus.Pending;
        StudentEmailStatus = BillArtifactOperationStatus.Pending;
        BillingEmailStatus = BillArtifactOperationStatus.Pending;
    }

    public void MarkPdfCompleted(string fileName, string blobName, string sha256, DateTime completedAt)
    {
        PdfFileName = fileName;
        PdfBlobName = blobName;
        PdfSha256 = sha256;
        PdfStatus = BillArtifactOperationStatus.Completed;
        LastAttemptAt = completedAt;
        LastError = null;
    }

    public void MarkPdfFailed(string error, DateTime attemptedAt)
    {
        PdfStatus = BillArtifactOperationStatus.Failed;
        RetryCount++;
        LastAttemptAt = attemptedAt;
        LastError = error.Length > 4000 ? error[..4000] : error;
    }

    public void MarkDocumentCreated(string documentTrackingId, DateTime attemptedAt)
    {
        ElementsDocumentTrackingId = documentTrackingId;
        LastAttemptAt = attemptedAt;
        LastError = null;
    }

    public void MarkDocumentUploadCompleted(DateTime completedAt)
    {
        DocumentUploadStatus = BillArtifactOperationStatus.Completed;
        LastAttemptAt = completedAt;
        LastError = null;
    }

    public void MarkDocumentUploadFailed(string error, DateTime attemptedAt)
    {
        DocumentUploadStatus = BillArtifactOperationStatus.Failed;
        RetryCount++;
        LastAttemptAt = attemptedAt;
        LastError = error.Length > 4000 ? error[..4000] : error;
    }
}
