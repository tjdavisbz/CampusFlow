using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Admin.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AssignmentsModel(IHousingWorkspaceAppService workspace) : CampusFlowPageModel
{
    public void OnGet() { }
    public async Task<JsonResult> OnGetPeriodsAsync() => new(await workspace.GetPeriodsAsync());
    public async Task<JsonResult> OnGetDraftAsync(int periodId) => new(await workspace.GetAsync(periodId));
    public async Task<JsonResult> OnGetStudentsAsync(string query) => new(await workspace.SearchStudentsAsync(query));
    public async Task<JsonResult> OnGetPreviewAsync(int periodId) => new(await workspace.PreviewAsync(periodId));
    public async Task<JsonResult> OnPostSaveAsync([FromBody] HousingWorkspaceState input) => new(await workspace.SaveAsync(input));
    public async Task<JsonResult> OnPostRefreshAsync([FromBody] RefreshInput input)
    {
        if (!input.Confirmed) throw new InvalidOperationException("Confirm replacing the draft before refreshing.");
        return new(await workspace.RefreshAsync(input.PeriodId, input.Revision));
    }
    public class RefreshInput
    {
        public int PeriodId { get; set; }
        public string? Revision { get; set; }
        public bool Confirmed { get; set; }
    }
}
