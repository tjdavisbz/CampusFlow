using System;
using System.Text.Json;
using System.Threading;
using Volo.Abp;
using Volo.Abp.Data;

namespace CampusFlow.Housing;

public sealed class HousingRecoveryDispatch
{
    private static readonly AsyncLocal<Guid?> Execution=new();
    public static Guid? CurrentExecutionId=>Execution.Value;
    public static IDisposable EnterExecution(Guid id)
    {var previous=Execution.Value;Execution.Value=id;return new DisposeAction(()=>Execution.Value=previous);}
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "Running";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Message { get; set; }
    public static HousingRecoveryDispatch? Read(HousingAssignmentDraft draft) =>
        draft.ExtraProperties.TryGetValue("HousingRecoveryDispatchV1",out var value) && value is not null
            ? JsonSerializer.Deserialize<HousingRecoveryDispatch>(value.ToString()!) : null;
    public void Save(HousingAssignmentDraft draft) => draft.SetProperty("HousingRecoveryDispatchV1",JsonSerializer.Serialize(this));
    public bool IsActive(HousingAssignmentDraft draft,DateTime now)
    {
        var updated=HousingApplyProgress.Read(draft)?.UpdatedAt;
        var heartbeat=updated is not null && updated>StartedAt?updated.Value:StartedAt;
        return Status=="Running" && now-heartbeat<TimeSpan.FromMinutes(30);
    }
}
