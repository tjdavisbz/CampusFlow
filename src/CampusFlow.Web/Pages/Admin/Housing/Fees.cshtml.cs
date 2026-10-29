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
public class FeesModel(IHousingWorkspaceAppService workspace, IHousingRateSetupAppService setup,
    IConfiguration configuration) : CampusFlowPageModel
{
    public string EnvironmentName =>
        string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase)
            ? "NON-PRODUCTION" : "PRODUCTION";
    public bool CanEdit =>
        (configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
         string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase)) ||
        (configuration.GetValue<bool>("Housing:EnableProductionWrites") &&
         string.IsNullOrWhiteSpace(configuration["App:EnvironmentLabel"]));

    public void OnGet() { }
    public async Task<JsonResult> OnGetPeriodsAsync() => new(await workspace.GetPeriodsAsync());
    public async Task<JsonResult> OnGetRatesAsync(int periodId) => new(await setup.GetAsync(periodId));
    public async Task<JsonResult> OnPostSaveAsync([FromBody] HousingRateScheduleDto input) => new(await setup.SaveAsync(input));
}
