using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using CampusFlow.Permissions;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CampusFlow.Web.Pages.Admin.StudentBilling;
[Authorize(CampusFlowPermissions.Admin.BillApproval)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class ClearanceResetsModel(IClearanceResetAppService service):CampusFlowPageModel
{
    public bool IsReadOnlyView=>HttpContext?.User.HasClaim(c=>c.Type==StudentViewClaimTypes.ExternalStudentId||c.Type==FacultyViewClaimTypes.FacultyId||c.Type==CampusFlow.AdvisorPortal.SchedulerViewClaimTypes.Email)==true;
    public void OnGet() { }
    public async Task<IActionResult> OnPostPreviewAsync(int? studentUid)
    {if(IsReadOnlyView)return Forbid();if(!ModelState.IsValid||studentUid is <=0)return BadRequest();return new JsonResult(await service.PreviewAsync(studentUid));}
    public async Task<IActionResult> OnPostRunAsync([FromBody]ClearanceResetRunRequestDto input)
    {if(IsReadOnlyView)return Forbid();if(!ModelState.IsValid)return BadRequest();return new JsonResult(await service.RunAsync(input));}
    public async Task<JsonResult> OnGetStatusAsync()=>new(await service.GetAsync());
    public async Task<IActionResult> OnPostScheduleAsync([FromBody]ClearanceResetUpdateDto input)
    {if(IsReadOnlyView)return Forbid();if(!ModelState.IsValid)return BadRequest();return new JsonResult(await service.SetAsync(input));}
}
