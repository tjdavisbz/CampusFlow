using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Housing;

public class MealPlanConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int? ExternalMealPlanId { get; private set; }
    public string ExternalMealPlanName { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string HousingChoicesJson { get; private set; } = "[]";
    public string EligibleAttendanceTypesJson { get; private set; } = "[]";
    public decimal? DisplayPrice { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsNoPlanOption { get; private set; }

    protected MealPlanConfiguration() { }

    public MealPlanConfiguration(Guid id, Guid? tenantId, int? externalMealPlanId,
        string externalMealPlanName, string displayName, string description,
        string housingChoicesJson, string eligibleAttendanceTypesJson,
        decimal? displayPrice, int sortOrder, bool isNoPlanOption = false) : base(id)
    {
        TenantId = tenantId;
        ExternalMealPlanId = externalMealPlanId;
        ExternalMealPlanName = externalMealPlanName;
        DisplayName = displayName;
        Description = description;
        HousingChoicesJson = housingChoicesJson;
        EligibleAttendanceTypesJson = eligibleAttendanceTypesJson;
        DisplayPrice = displayPrice;
        SortOrder = sortOrder;
        IsNoPlanOption = isNoPlanOption;
        IsActive = true;
    }
}
