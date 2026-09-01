namespace CampusFlow.BillApprovals;

public enum BillApprovalStatus
{
    Draft = 0,
    AwaitingPayment = 1,
    Approved = 2,
    DocumentPending = 3,
    Completed = 4,
    Failed = 5
}

public enum BillPaymentChoice
{
    None = 0,
    PayNow = 1,
    DeferredPaymentPlan = 2,
    NoBalanceDue = 3
}

public enum BillArtifactOperationStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    NotRequired = 3
}
