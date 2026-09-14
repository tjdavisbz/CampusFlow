using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.BillApprovals;

public interface IResetBillApprovalAppService : IApplicationService
{
    Task<ResetBillApprovalSearchResultDto> SearchAsync(string? termCode, string? query);
    Task<ResetBillApprovalDto> GetAsync(Guid id);
    Task ResetAsync(Guid id);
}
