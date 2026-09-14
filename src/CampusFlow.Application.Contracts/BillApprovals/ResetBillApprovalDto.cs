using System;

namespace CampusFlow.BillApprovals;

public class ResetBillApprovalDto
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string TermCode { get; set; } = string.Empty;
    public string TermName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? AcceptedAt { get; set; }
}
