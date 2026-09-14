using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Housing;

public class HousingAssignmentDraft : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int PeriodId { get; private set; }
    public string BaselineJson { get; private set; } = "[]";
    public string RoomsJson { get; private set; } = "[]";
    public DateTime RefreshedAt { get; private set; }

    protected HousingAssignmentDraft() { }

    public HousingAssignmentDraft(Guid id, Guid tenantId, int periodId, string roomsJson, DateTime refreshedAt) : base(id)
    {
        TenantId = tenantId;
        PeriodId = periodId;
        Refresh(roomsJson, refreshedAt);
    }

    public void Save(string roomsJson) => RoomsJson = roomsJson;

    public void Refresh(string roomsJson, DateTime refreshedAt)
    {
        BaselineJson = RoomsJson = roomsJson;
        RefreshedAt = refreshedAt;
    }
}
