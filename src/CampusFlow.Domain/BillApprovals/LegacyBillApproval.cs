using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class LegacyBillApproval : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string StudentId { get; private set; } = null!;
    public string ExternalTermId { get; private set; } = null!;
    public string TermCode { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public bool SourceApproved { get; private set; }
    public DateTime? LoginTime { get; private set; }
    public DateTime? AcceptTime { get; private set; }
    public string SourceSha256 { get; private set; } = null!;
    public int SourceRowNumber { get; private set; }

    protected LegacyBillApproval() { }

    public LegacyBillApproval(Guid id, Guid tenantId, string studentUid, string studentId,
        string termId, string termCode, string termName, bool approved, DateTime? loginTime,
        DateTime? acceptTime, string sourceSha256, int sourceRowNumber) : base(id)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(studentUid) ||
            string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(termId) ||
            sourceSha256.Length != 64 || sourceRowNumber < 2)
            throw new ArgumentException("A legacy approval requires tenant, source identity and import provenance.");
        TenantId = tenantId;
        ExternalStudentId = studentUid;
        StudentId = studentId;
        ExternalTermId = termId;
        TermCode = termCode;
        TermName = termName;
        SourceApproved = approved;
        // The source has no timezone offset. Preserve its wall-clock values exactly.
        LoginTime = loginTime.HasValue ? DateTime.SpecifyKind(loginTime.Value, DateTimeKind.Unspecified) : null;
        AcceptTime = acceptTime.HasValue ? DateTime.SpecifyKind(acceptTime.Value, DateTimeKind.Unspecified) : null;
        SourceSha256 = sourceSha256;
        SourceRowNumber = sourceRowNumber;
    }

    public bool CountsAsApproved => SourceApproved && AcceptTime.HasValue && ExternalTermId != "0";
}
