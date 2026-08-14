using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class AdvisorAssignment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string AttendanceType { get; private set; } = null!;
    public string ExternalAdvisorId { get; private set; } = null!;
    public string AdvisorEmail { get; private set; } = null!;
    public string AdvisorDisplayName { get; private set; } = null!;
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }

    protected AdvisorAssignment() { }

    public AdvisorAssignment(Guid id, Guid? tenantId, string attendanceType,
        string externalAdvisorId, string advisorEmail, string advisorDisplayName,
        DateTime effectiveFrom, bool isActive = true) : base(id)
    {
        TenantId = tenantId;
        AttendanceType = attendanceType;
        ExternalAdvisorId = externalAdvisorId;
        AdvisorEmail = advisorEmail;
        AdvisorDisplayName = advisorDisplayName;
        EffectiveFrom = effectiveFrom;
        IsActive = isActive;
    }
}
