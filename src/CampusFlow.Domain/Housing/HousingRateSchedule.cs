using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Housing;

public class HousingRateSchedule : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int PeriodId { get; private set; }
    public int TermCalendarId { get; private set; }
    public string RatesJson { get; private set; } = "[]";

    protected HousingRateSchedule() { }

    public HousingRateSchedule(Guid id, Guid tenantId, int periodId, int termCalendarId, string ratesJson) : base(id)
    {
        TenantId = tenantId;
        PeriodId = periodId;
        TermCalendarId = termCalendarId;
        RatesJson = ratesJson;
    }

    public void Update(string ratesJson) => RatesJson = ratesJson;
}
