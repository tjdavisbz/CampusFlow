using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Housing;

public class StudentHousingSelection : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public DateTime? TermStartDate { get; private set; }
    public DateTime? TermEndDate { get; private set; }
    public HousingChoice HousingChoice { get; private set; }
    public int? ExternalMealPlanId { get; private set; }
    public string MealPlanName { get; private set; } = null!;
    public DateTime SubmittedAt { get; private set; }
    public bool SyncedToStudentInformationSystem { get; private set; }
    public DateTime? SyncedAt { get; private set; }
    public string? LastSyncError { get; private set; }

    protected StudentHousingSelection() { }

    public StudentHousingSelection(Guid id, Guid? tenantId, Guid studentProfileId,
        string externalStudentId, string termName, DateTime? termStartDate, DateTime? termEndDate,
        HousingChoice housingChoice, int? externalMealPlanId, string mealPlanName) : base(id)
    {
        TenantId = tenantId;
        StudentProfileId = studentProfileId;
        ExternalStudentId = externalStudentId;
        TermName = termName;
        TermStartDate = termStartDate;
        TermEndDate = termEndDate;
        Update(housingChoice, externalMealPlanId, mealPlanName);
    }

    public void Update(HousingChoice housingChoice, int? externalMealPlanId, string mealPlanName)
    {
        HousingChoice = housingChoice;
        ExternalMealPlanId = externalMealPlanId;
        MealPlanName = mealPlanName;
        SubmittedAt = DateTime.UtcNow;
        SyncedToStudentInformationSystem = false;
        SyncedAt = null;
        LastSyncError = null;
    }

    public void MarkSynced()
    {
        SyncedToStudentInformationSystem = true;
        SyncedAt = DateTime.UtcNow;
        LastSyncError = null;
    }

    public void MarkPending(string error)
    {
        SyncedToStudentInformationSystem = false;
        LastSyncError = error.Length > 4000 ? error[..4000] : error;
    }
}
