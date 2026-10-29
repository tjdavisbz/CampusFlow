using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class StudentGraduationIntent : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalTermId { get; private set; } = null!;
    public bool IntendsToGraduate { get; private set; }

    protected StudentGraduationIntent() { }

    public StudentGraduationIntent(Guid id, Guid? tenantId, Guid studentProfileId,
        string externalTermId, bool intendsToGraduate) : base(id)
    {
        TenantId = tenantId;
        StudentProfileId = studentProfileId;
        ExternalTermId = externalTermId;
        IntendsToGraduate = intendsToGraduate;
    }

    public void Update(bool intendsToGraduate) => IntendsToGraduate = intendsToGraduate;
}
