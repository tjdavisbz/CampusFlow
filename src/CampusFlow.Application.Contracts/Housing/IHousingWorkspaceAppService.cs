using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.Housing;

public interface IHousingWorkspaceAppService : IApplicationService
{
    Task<List<HousingPeriodOption>> GetPeriodsAsync();
    Task<HousingWorkspaceState?> GetAsync(int periodId);
    Task<HousingWorkspaceState> RefreshAsync(int periodId, string? revision);
    Task<HousingWorkspaceState> SaveAsync(HousingWorkspaceState input);
    Task<List<HousingWorkspaceChange>> PreviewAsync(int periodId);
    Task<string> GetRateRevisionAsync(int periodId);
    Task<HousingWorkspaceState> ApplyAssignmentsAsync(ApplyHousingDraftInput input);
    Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId);
    Task<HousingTermCopyPreview> PreviewTermCopyAsync(int sourcePeriodId, int destinationPeriodId);
    Task<HousingWorkspaceState> CopyTermDraftAsync(CopyHousingTermInput input);
}
