using System.Linq;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Components.Toolbar.FacultyView;

public class FacultyViewViewComponent(IHttpContextAccessor httpContextAccessor) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var claims = httpContextAccessor.HttpContext?.User.Claims;
        string? Get(string type) => claims?.FirstOrDefault(x => x.Type == type)?.Value;
        return View("Default", new FacultyViewToolbarModel(
            Get(FacultyViewClaimTypes.FacultyName) ?? "Faculty member",
            Get(FacultyViewClaimTypes.FacultyId) ?? string.Empty));
    }
}

public sealed record FacultyViewToolbarModel(string FacultyName, string FacultyId);
