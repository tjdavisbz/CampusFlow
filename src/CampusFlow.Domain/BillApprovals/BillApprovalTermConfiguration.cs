using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class BillApprovalTermConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string ExternalTermId { get; private set; } = null!;
    public string TermCode { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public DateTime OpensAt { get; private set; }
    public DateTime ClosesAt { get; private set; }
    public bool IsEnabled { get; private set; }
    public Guid AgreementTemplateId { get; private set; }
    public Guid PaymentPlanPolicyId { get; private set; }

    protected BillApprovalTermConfiguration() { }

    public BillApprovalTermConfiguration(Guid id, Guid? tenantId, string externalTermId,
        string termCode, string termName, DateTime opensAt, DateTime closesAt, bool isEnabled,
        Guid agreementTemplateId, Guid paymentPlanPolicyId) : base(id)
    {
        TenantId = tenantId;
        ExternalTermId = externalTermId;
        TermCode = termCode;
        TermName = termName;
        Update(opensAt, closesAt, isEnabled, agreementTemplateId, paymentPlanPolicyId);
    }

    public void Update(DateTime opensAt, DateTime closesAt, bool isEnabled,
        Guid agreementTemplateId, Guid paymentPlanPolicyId)
    {
        if (closesAt <= opensAt) throw new ArgumentException("Bill Approval must close after it opens.");
        OpensAt = opensAt;
        ClosesAt = closesAt;
        IsEnabled = isEnabled;
        AgreementTemplateId = agreementTemplateId;
        PaymentPlanPolicyId = paymentPlanPolicyId;
    }

    public bool IsOpen(DateTime at) => IsEnabled && OpensAt <= at && ClosesAt >= at;
}
