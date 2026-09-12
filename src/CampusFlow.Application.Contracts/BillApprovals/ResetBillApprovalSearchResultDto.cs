using System.Collections.Generic;

namespace CampusFlow.BillApprovals;

public class ResetBillApprovalSearchResultDto
{
    public List<ResetBillApprovalTermDto> Terms { get; set; } = [];
    public List<ResetBillApprovalDto> Approvals { get; set; } = [];
}

public class ResetBillApprovalTermDto
{
    public string TermCode { get; set; } = string.Empty;
    public string TermName { get; set; } = string.Empty;
}
