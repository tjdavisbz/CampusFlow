using System;
namespace CampusFlow.BillApprovals;
public static class BillApprovalAcceptanceRules
{
    public static bool IsApproved(DateTime? acceptedAt,BillApprovalStatus status)=>acceptedAt.HasValue&&status is
        BillApprovalStatus.Approved or BillApprovalStatus.DocumentPending or BillApprovalStatus.Completed;
}
