using System.Threading.Tasks;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class EndStudentViewModel : CampusFlowPageModel
{
    private readonly StudentViewSession _session;
    private readonly ILogger<EndStudentViewModel> _logger;

    public EndStudentViewModel(StudentViewSession session, ILogger<EndStudentViewModel> logger)
    {
        _session = session;
        _logger = logger;
    }

    public IActionResult OnGet() => RedirectToPage("/Admin/ImpersonateStudent");

    public IActionResult OnPost()
    {
        _logger.LogWarning(
            "Administrator {ActorUserId} ended read-only student view. TraceId={TraceId}",
            CurrentUser.Id, HttpContext.TraceIdentifier);
        _session.End(HttpContext);
        return RedirectToPage("/Admin/ImpersonateStudent");
    }
}
