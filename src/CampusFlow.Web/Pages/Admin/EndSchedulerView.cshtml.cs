using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class EndSchedulerViewModel(SchedulerViewSession session,
    ILogger<EndSchedulerViewModel> logger) : CampusFlowPageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/ImpersonateScheduler");

    public IActionResult OnPost()
    {
        logger.LogWarning("Administrator {ActorUserId} ended read-only scheduler view. TraceId={TraceId}",
            CurrentUser.Id, HttpContext.TraceIdentifier);
        session.End(HttpContext);
        return RedirectToPage("/Admin/ImpersonateScheduler");
    }
}
