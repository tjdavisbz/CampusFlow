namespace CampusFlow.BillApprovals;

public static class BillApprovalAccessRules
{
    public static bool HasBlockingPreviousBalance(decimal previousBalance) => previousBalance > 50m;
}
