using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.Pages.Admin.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AssignmentsModel(IHousingWorkspaceAppService workspace, IConfiguration configuration) : CampusFlowPageModel
{
    public string EnvironmentName =>
        string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase)
            ? "NON-PRODUCTION" : "PRODUCTION";
    public bool CanApplyToElements =>
        (configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
         string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase)) ||
        (configuration.GetValue<bool>("Housing:EnableProductionWrites") &&
         string.IsNullOrWhiteSpace(configuration["App:EnvironmentLabel"]));
    public void OnGet() { }
    public async Task<JsonResult> OnGetPeriodsAsync() => new(await workspace.GetPeriodsAsync());
    public async Task<JsonResult> OnGetDraftAsync(int periodId) => new(await workspace.GetAsync(periodId));
    public async Task<JsonResult> OnGetStudentsAsync(string query, int periodId) => new(await workspace.SearchStudentsAsync(query, periodId));
    public async Task<JsonResult> OnGetPreviewAsync(int periodId) => new(await workspace.PreviewAsync(periodId));
    public async Task<JsonResult> OnGetRateRevisionAsync(int periodId) => new(await workspace.GetRateRevisionAsync(periodId));
    public async Task<JsonResult> OnPostSaveAsync([FromBody] HousingWorkspaceState input) => new(await workspace.SaveAsync(input));
    public async Task<JsonResult> OnPostApplyAsync([FromBody] ApplyHousingDraftInput input) => new(await workspace.ApplyAssignmentsAsync(input));
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
