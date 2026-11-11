using System;
using System.Collections.Generic;
using System.Text.Json;
using Volo.Abp.Data;
namespace CampusFlow.Housing;

public class HousingApplyProgress
{
    public Guid RunId { get; set; }
    public string Status { get; set; } = "Running";
    public DateTime StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RecoveryOfRunId { get; set; }
    public string? ReviewReason { get; set; }
    public string? FailureCode { get; set; }
    public int? FailureHttpStatus { get; set; }
    public string BeforeJson { get; set; } = "[]";
    public string DesiredJson { get; set; } = "[]";
    public List<string> Operations { get; set; } = [];
    public static HousingApplyProgress? Read(HousingAssignmentDraft draft) =>
        draft.ExtraProperties.TryGetValue("HousingApplyV1",out var value) && value is not null
            ? JsonSerializer.Deserialize<HousingApplyProgress>(value.ToString()!) : null;
    public void Save(HousingAssignmentDraft draft)=>draft.SetProperty("HousingApplyV1",JsonSerializer.Serialize(this));
    public static void Archive(HousingAssignmentDraft draft, HousingApplyProgress progress)
    {
        var history = draft.ExtraProperties.TryGetValue("HousingApplyHistoryV1", out var value) && value is not null
            ? JsonSerializer.Deserialize<List<HousingApplyProgress>>(value.ToString()!)! : [];
        if (history.Count >= 20) throw new InvalidOperationException("Housing recovery history needs administrator review before another attempt.");
        history.Add(progress);
        draft.SetProperty("HousingApplyHistoryV1", JsonSerializer.Serialize(history));
    }
    public static void EnsureEditable(HousingAssignmentDraft draft,DateTime now,bool refreshing=false)
    {
        var p=Read(draft);if(p is null||p.Status is "Completed" or "Reviewed")return;
        if (refreshing && p.Status == "Needs review")
            throw new InvalidOperationException("This partial apply still has a saved plan. Review recovery to retain its assignments; Refresh cannot discard it.");
        if(!refreshing || (p.Status=="Running" && now-(p.UpdatedAt??p.StartedAt)<TimeSpan.FromMinutes(30)))
            throw new InvalidOperationException("A housing apply is running or needs review. Check live assignments and charges, then refresh after the operation has stopped before preparing another apply.");
    }
}
