using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class CourseSectionAttendanceTypeMapping : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int SectionStart { get; private set; }
    public int SectionEnd { get; private set; }
    public int ExternalAttendanceTypeId { get; private set; }
    public string AttendanceType { get; private set; } = null!;
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }

    protected CourseSectionAttendanceTypeMapping() { }

    public CourseSectionAttendanceTypeMapping(Guid id, Guid? tenantId, int sectionStart, int sectionEnd,
        int externalAttendanceTypeId, string attendanceType, DateTime effectiveFrom,
        bool isActive = true) : base(id)
    {
        if (sectionStart < 0 || sectionEnd < sectionStart)
            throw new ArgumentOutOfRangeException(nameof(sectionEnd));
        TenantId = tenantId;
        SectionStart = sectionStart;
        SectionEnd = sectionEnd;
        ExternalAttendanceTypeId = externalAttendanceTypeId;
        AttendanceType = attendanceType;
        EffectiveFrom = effectiveFrom;
        IsActive = isActive;
    }

    public bool Includes(int section, DateTime at) =>
        IsActive && section >= SectionStart && section <= SectionEnd &&
        EffectiveFrom <= at && (EffectiveTo is null || EffectiveTo > at);
}
