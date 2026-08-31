using CampusFlow.BillApprovals;

namespace CampusFlow.Web.BillApprovals;

public interface IBillApprovalPdfGenerator
{
    byte[] Generate(BillApproval approval);
}
