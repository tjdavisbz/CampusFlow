using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Chapel;

public class ChapelGroupMembership : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int TermCalendarId { get; private set; }
    public int StudentUid { get; private set; }
    public string GroupName { get; private set; } = "";

    protected ChapelGroupMembership() { }

    public ChapelGroupMembership(Guid id, Guid? tenantId, int termCalendarId, int studentUid, string groupName) : base(id)
    {
        if (termCalendarId <= 0) throw new ArgumentOutOfRangeException(nameof(termCalendarId));
        if (studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 100)
            throw new ArgumentException("A group name is required.", nameof(groupName));
        TenantId = tenantId;
        TermCalendarId = termCalendarId;
        StudentUid = studentUid;
        GroupName = groupName.Trim();
    }
}
