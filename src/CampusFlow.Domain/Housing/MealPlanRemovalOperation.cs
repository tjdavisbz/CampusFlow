using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Housing;

public interface IMealPlanRemovalGateway
{
    Task<List<MealPlanRemovalEntry>> ReviewAsync(MealPlanRemovalTarget target);
    Task DeleteAsync(MealPlanRemovalTarget target, MealPlanRemovalEntry entry);
}

public interface IMealPlanRemovalCoordinator
{
    Task<MealPlanRemovalReview> ReviewAsync(MealPlanRemovalTarget target);
    Task RemoveAsync(MealPlanRemovalTarget target, string revision, string reason);
    Task<List<MealPlanRemovalTarget>> PendingAsync(int studentUid, int termId);
    Task<IAsyncDisposable> LockAsync(int studentUid, int termId);
}

public sealed class MealPlanRemovalOperation : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int StudentUid { get; private set; }
    public int TermId { get; private set; }
    public int AssignmentId { get; private set; }
    public string TargetJson { get; private set; } = "";
    public string EntriesJson { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public string State { get; private set; } = "Pending";
    public string Stage { get; private set; } = "Reviewed removal saved";
    private MealPlanRemovalOperation() { }
    public MealPlanRemovalOperation(Guid id, Guid tenant, MealPlanRemovalTarget target, string entriesJson, string reason) : base(id)
    {
        if (tenant == Guid.Empty || target.StudentUid <= 0 || target.TermId <= 0 || target.AssignmentId <= 0 || target.MealPlanId <= 0 || target.Amount < 0 || reason.Trim().Length is < 10 or > 500)
            throw new ArgumentException("Review the meal removal identity and enter a reason of 10–500 characters.");
        TenantId = tenant; StudentUid = target.StudentUid; TermId = target.TermId; AssignmentId = target.AssignmentId;
        TargetJson = System.Text.Json.JsonSerializer.Serialize(target); EntriesJson = entriesJson; Reason = reason.Trim();
    }
    public void Record(string stage) { if (State == "Completed") throw new InvalidOperationException("Removal is already complete."); Stage = stage; }
    public void Complete() { State = "Completed"; Stage = "Assignment and reviewed unposted charges removed"; }
    public void NeedsReview() { State = "Needs review"; }
}
