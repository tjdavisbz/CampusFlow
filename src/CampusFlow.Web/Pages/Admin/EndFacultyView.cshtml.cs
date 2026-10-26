using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class EndFacultyViewModel(FacultyViewSession session,
    ILogger<EndFacultyViewModel> logger) : CampusFlowPageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/ImpersonateFaculty");

    public IActionResult OnPost()
    {
        logger.LogWarning("Administrator {ActorUserId} ended read-only faculty view. TraceId={TraceId}",
            CurrentUser.Id, HttpContext.TraceIdentifier);
        session.End(HttpContext);
        return RedirectToPage("/Admin/ImpersonateFaculty");
    }
}
