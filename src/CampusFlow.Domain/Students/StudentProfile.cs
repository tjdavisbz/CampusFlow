using System;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Students;

public class StudentProfile : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public StudentInformationSystemProvider Provider { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string StudentId { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string? PreferredName { get; private set; }
    public string LastName { get; private set; } = null!;

    protected StudentProfile()
    {
    }

    public StudentProfile(
        Guid id,
        Guid? tenantId,
        Guid userId,
        StudentInformationSystemStudent student)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Update(student);
    }

    public void Update(StudentInformationSystemStudent student)
    {
        Provider = student.Provider;
        ExternalStudentId = student.ExternalStudentId;
        StudentId = student.StudentId;
        Email = student.Email;
        FirstName = student.FirstName;
        PreferredName = student.PreferredName;
        LastName = student.LastName;
    }

    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(PreferredName) ? FirstName : PreferredName)} {LastName}".Trim();
}
