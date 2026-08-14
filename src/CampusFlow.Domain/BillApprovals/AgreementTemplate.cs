using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class AgreementTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Version { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public string ContentHtml { get; private set; } = null!;
    public string AllowedMergeFieldsJson { get; private set; } = "[]";
    public bool IsPublished { get; private set; }

    protected AgreementTemplate() { }

    public AgreementTemplate(Guid id, Guid? tenantId, string name, int version, DateTime effectiveFrom,
        string contentHtml, string allowedMergeFieldsJson, bool isPublished) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Version = version;
        EffectiveFrom = effectiveFrom;
        ContentHtml = contentHtml;
        AllowedMergeFieldsJson = allowedMergeFieldsJson;
        IsPublished = isPublished;
    }
}
