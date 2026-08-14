using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class PaymentPlanPolicy : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Version { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public decimal EnrollmentFee { get; private set; }
    public decimal PartTimeBalanceDivisor { get; private set; }
    public decimal ResidentialMinimumPayment { get; private set; }
    public decimal StandardMinimumPayment { get; private set; }
    public string ResidentialAttendanceTypesJson { get; private set; } = "[]";
    public string FallDueDatesJson { get; private set; } = "[]";
    public string SpringDueDatesJson { get; private set; } = "[]";
    public string SummerDueDatesJson { get; private set; } = "[]";
    public bool IsPublished { get; private set; }

    protected PaymentPlanPolicy() { }

    public PaymentPlanPolicy(Guid id, Guid? tenantId, string name, int version, DateTime effectiveFrom,
        decimal enrollmentFee, decimal partTimeBalanceDivisor, decimal residentialMinimumPayment,
        decimal standardMinimumPayment, string residentialAttendanceTypesJson, string fallDueDatesJson,
        string springDueDatesJson, string summerDueDatesJson, bool isPublished) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Version = version;
        EffectiveFrom = effectiveFrom;
        EnrollmentFee = enrollmentFee;
        PartTimeBalanceDivisor = partTimeBalanceDivisor;
        ResidentialMinimumPayment = residentialMinimumPayment;
        StandardMinimumPayment = standardMinimumPayment;
        ResidentialAttendanceTypesJson = residentialAttendanceTypesJson;
        FallDueDatesJson = fallDueDatesJson;
        SpringDueDatesJson = springDueDatesJson;
        SummerDueDatesJson = summerDueDatesJson;
        IsPublished = isPublished;
    }
}
