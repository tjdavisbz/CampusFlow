using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Admin.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TermCopyModel(IHousingWorkspaceAppService workspace) : CampusFlowPageModel
{
    public void OnGet() { }
    public async Task<JsonResult> OnGetPeriodsAsync() => new(await workspace.GetPeriodsAsync());
    public async Task<JsonResult> OnGetPreviewAsync(int sourcePeriodId, int destinationPeriodId) =>
        new(await workspace.PreviewTermCopyAsync(sourcePeriodId, destinationPeriodId));
    public async Task<JsonResult> OnPostCopyAsync([FromBody] CopyHousingTermInput input) =>
        new(await workspace.CopyTermDraftAsync(input));
}
