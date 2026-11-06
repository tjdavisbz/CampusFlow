using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Data;

namespace CampusFlow.BillApprovals;

public enum BillCompletionStep { Pdf, Cleared, Document, BookstoreEmail, StudentEmail }
public enum BillCompletionStatus { Pending, Running, Completed, Failed, Uncertain, Disabled }
public sealed class BillCompletionStepState
{
    public BillCompletionStatus Status { get; set; }
    public DateTime? AttemptedAt { get; set; }
    public string? Error { get; set; }
    public Guid? ReconciledBy { get; set; }
    public DateTime? ReconciledAt { get; set; }
}
public sealed class BillApprovalCompletionProgress
{
    public Guid? ActiveRunId { get; set; }
    public DateTime? StartedAt { get; set; }
    public Dictionary<BillCompletionStep, BillCompletionStepState> Steps { get; set; } = new();
    public BillCompletionStepState Step(BillCompletionStep step)
    {
        if (!Steps.TryGetValue(step,out var state)) Steps[step] = state = new();
        return state;
    }
    public bool IsComplete => Enum.GetValues<BillCompletionStep>().All(x => Step(x).Status == BillCompletionStatus.Completed);
    public static bool IsEmail(BillCompletionStep step) => step is BillCompletionStep.BookstoreEmail or BillCompletionStep.StudentEmail;
    public static BillApprovalCompletionProgress Read(BillApproval approval) =>
        JsonSerializer.Deserialize<BillApprovalCompletionProgress>(approval.GetProperty<string>("BillApprovalCompletionV1") ?? "{}") ?? new();
    public void Save(BillApproval approval) => approval.SetProperty("BillApprovalCompletionV1", JsonSerializer.Serialize(this));
    public bool Begin(Guid runId, DateTime now)
    {
        if (ActiveRunId.HasValue && StartedAt.HasValue && StartedAt > now.AddMinutes(-30)) return false;
        foreach (var step in Enum.GetValues<BillCompletionStep>())
            if (Step(step).Status == BillCompletionStatus.Running)
            {
                Step(step).Status = IsEmail(step) ? BillCompletionStatus.Uncertain : BillCompletionStatus.Pending;
                Step(step).Error = IsEmail(step) ? "The sending result is unknown. Check the mail service delivery logs before retrying." : null;
            }
        ActiveRunId = runId;StartedAt = now;return true;
    }
    public bool Start(BillCompletionStep step, DateTime now)
    {
        var state = Step(step);
        if (state.Status is BillCompletionStatus.Completed or BillCompletionStatus.Uncertain or BillCompletionStatus.Running) return false;
        if (step == BillCompletionStep.Document && Step(BillCompletionStep.Pdf).Status != BillCompletionStatus.Completed) return false;
        if (IsEmail(step) && (Step(BillCompletionStep.Cleared).Status != BillCompletionStatus.Completed || Step(BillCompletionStep.Pdf).Status != BillCompletionStatus.Completed)) return false;
        state.Status = BillCompletionStatus.Running;state.AttemptedAt = now;state.Error = null;return true;
    }
    public void Finish(BillCompletionStep step, BillCompletionStatus status, string? error = null)
    { var state=Step(step);state.Status=status;state.Error=error; }
    public void ResetForRetry(DateTime now, bool confirmedUnsent)
    {
        if (ActiveRunId.HasValue && StartedAt > now.AddMinutes(-30)) throw new InvalidOperationException("Completion is still running. Please wait.");
        Begin(Guid.NewGuid(),now);ActiveRunId=null;
        foreach(var step in Enum.GetValues<BillCompletionStep>())
        {
            var state=Step(step);
            if (state.Status==BillCompletionStatus.Uncertain && !confirmedUnsent) continue;
            if (state.Status!=BillCompletionStatus.Completed) {state.Status=BillCompletionStatus.Pending;state.Error=null;}
        }
    }
}
public sealed class BillApprovalCompletionJobArgs
{
    public Guid TenantId { get; set; }
    public Guid ApprovalId { get; set; }
    public BillApprovalCompletionJobArgs() { }
    public BillApprovalCompletionJobArgs(Guid tenantId, Guid approvalId) {TenantId=tenantId;ApprovalId=approvalId;}
}
public sealed class BillCompletionPreparationException(string message) : Exception(message);
