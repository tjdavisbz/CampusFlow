using System;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using CampusFlow.Permissions;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CampusFlow.Web.Pages.Admin.StudentBilling;
[Authorize(CampusFlowPermissions.Admin.CostTypes)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class CostTypesModel(ICostTypeAppService service) : CampusFlowPageModel
{
    public bool IsReadOnlyView => HttpContext?.User.HasClaim(claim =>
        claim.Type == StudentViewClaimTypes.ExternalStudentId ||
        claim.Type == FacultyViewClaimTypes.FacultyId ||
        claim.Type == CampusFlow.AdvisorPortal.SchedulerViewClaimTypes.Email) == true;
    public void OnGet() { }
    public async Task<JsonResult> OnGetHistoryAsync() => new(await service.GetHistoryAsync());
    public async Task<JsonResult> OnGetSetupAsync() => new(await service.GetAsync());
    public async Task<JsonResult> OnPostSaveAsync([FromBody] CostTypeSetupDto input) => new(await service.SaveAsync(input));
    public async Task<JsonResult> OnPostPreviewAsync(int? studentUid)
    {
        // A malformed or overflowing UID must not bind to null and preview every student.
        if (!ModelState.IsValid || studentUid is <= 0)
            return new JsonResult(new { message = "Enter a valid Elements UID, not the longer Student ID, or leave it blank to preview all accounts." }) { StatusCode = 400 };
        return new(await service.PreviewAsync(studentUid));
    }
    public async Task<JsonResult> OnPostRunAsync(Guid runId) => new(await service.RunAsync(runId));
}
