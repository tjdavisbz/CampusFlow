using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class BillApproval : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string StudentId { get; private set; } = null!;
    public string ExternalTermId { get; private set; } = null!;
    public string TermCode { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public BillPaymentChoice PaymentChoice { get; private set; }
    public BillApprovalStatus Status { get; private set; }
    public decimal ChargesTotal { get; private set; }
    public decimal CreditsTotal { get; private set; }
    public decimal AnticipatedAidTotal { get; private set; }
    public decimal RemainingBalance { get; private set; }
    public decimal PaymentPlanFee { get; private set; }
    public string PaymentScheduleJson { get; private set; } = "[]";
    public string ReviewSnapshotJson { get; private set; } = "{}";
    public Guid? AgreementTemplateId { get; private set; }
    public int? AgreementTemplateVersion { get; private set; }
    public string? RenderedAgreementSnapshot { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public string? SourceIp { get; private set; }
    public string? UserAgent { get; private set; }

    protected BillApproval() { }

    public BillApproval(Guid id, Guid? tenantId, Guid userId, Guid studentProfileId, string externalStudentId,
        string studentId, string externalTermId, string termCode, string termName) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        StudentProfileId = studentProfileId;
        ExternalStudentId = externalStudentId;
        StudentId = studentId;
        ExternalTermId = externalTermId;
        TermCode = termCode;
        TermName = termName;
        Status = BillApprovalStatus.Draft;
    }

    public void UpdateDraft(BillPaymentChoice paymentChoice, decimal chargesTotal, decimal creditsTotal,
        decimal anticipatedAidTotal, decimal remainingBalance, decimal paymentPlanFee, string paymentScheduleJson,
        string reviewSnapshotJson)
    {
        if (AcceptedAt is not null) return;
        PaymentChoice = paymentChoice;
        ChargesTotal = chargesTotal;
        CreditsTotal = creditsTotal;
        AnticipatedAidTotal = anticipatedAidTotal;
        RemainingBalance = remainingBalance;
        PaymentPlanFee = paymentPlanFee;
        PaymentScheduleJson = paymentScheduleJson;
        ReviewSnapshotJson = reviewSnapshotJson;
    }

    public void Accept(Guid agreementTemplateId, int agreementTemplateVersion, string renderedAgreementSnapshot,
        DateTime acceptedAt, string? sourceIp, string? userAgent)
    {
        if (AcceptedAt is not null) return;
        AgreementTemplateId = agreementTemplateId;
        AgreementTemplateVersion = agreementTemplateVersion;
        RenderedAgreementSnapshot = renderedAgreementSnapshot;
        AcceptedAt = acceptedAt;
        SourceIp = sourceIp;
        UserAgent = userAgent;
        Status = RemainingBalance > 0 && PaymentChoice == BillPaymentChoice.PayNow
            ? BillApprovalStatus.AwaitingPayment
            : BillApprovalStatus.Approved;
    }
}
