using System.Linq;
using CampusFlow.Web.Portals;
using CampusFlow.AdvisorPortal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Components.Toolbar.SchedulerView;

public class SchedulerViewViewComponent(IHttpContextAccessor httpContextAccessor) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var claims = httpContextAccessor.HttpContext?.User.Claims;
        string? Get(string type) => claims?.FirstOrDefault(x => x.Type == type)?.Value;
        return View("Default", new SchedulerViewToolbarModel(
            Get(SchedulerViewClaimTypes.Name) ?? "Scheduler",
            Get(SchedulerViewClaimTypes.Email) ?? string.Empty));
    }
}

public sealed record SchedulerViewToolbarModel(string Name, string Email);
