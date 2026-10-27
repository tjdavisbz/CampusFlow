using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Students;

/// <summary>An administrator-approved staff login's access to an existing student profile.</summary>
public class StaffStudentLink : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid StudentProfileId { get; private set; }

    protected StaffStudentLink() { }

    public StaffStudentLink(Guid id, Guid? tenantId, Guid staffUserId, Guid studentProfileId) : base(id)
    {
        TenantId = tenantId;
        StaffUserId = staffUserId;
        StudentProfileId = studentProfileId;
    }

    public void ChangeStudent(Guid studentProfileId) => StudentProfileId = studentProfileId;
}
