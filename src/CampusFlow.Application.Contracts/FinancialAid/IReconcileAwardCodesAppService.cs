using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Application.Services;

namespace CampusFlow.FinancialAid;

public interface IReconcileAwardCodesAppService : IApplicationService
{
    Task<bool> CanApplyAsync();
    Task<IReadOnlyList<string>> GetTermsAsync();
    Task<IReadOnlyList<AwardCodeMismatch>> PreviewAsync(string term);
    Task<ReconcileAwardCodesResult> ApplyAsync(string term, string previewAwardIds);
}

public record ReconcileAwardCodesResult(int Updated, string? Error);
