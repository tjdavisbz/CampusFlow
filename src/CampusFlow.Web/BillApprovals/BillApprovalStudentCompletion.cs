using CampusFlow.BillApprovals;

namespace CampusFlow.Web.BillApprovals;

public static class BillApprovalStudentCompletion
{
    public const string CompleteMessage = "Your bill has been approved, and your registration is complete!";
    public const string PendingMessage = "Your bill has been approved. We are finishing your registration; contact Student Billing if this message remains.";
    public static bool RegistrationComplete(BillApproval approval) =>
        BillApprovalAcceptanceRules.IsApproved(approval.AcceptedAt, approval.Status) &&
        BillApprovalCompletionProgress.Read(approval).Step(BillCompletionStep.Cleared).Status == BillCompletionStatus.Completed;
}
