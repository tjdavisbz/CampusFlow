using System.Linq;
using CampusFlow.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Components.Toolbar.StudentView;

public class StudentViewViewComponent : ViewComponent
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public StudentViewViewComponent(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public IViewComponentResult Invoke()
    {
        var claims = _httpContextAccessor.HttpContext?.User.Claims;
        string? Get(string type) => claims?.FirstOrDefault(x => x.Type == type)?.Value;
        var name = $"{(Get(StudentViewClaimTypes.PreferredName) is { Length: > 0 } preferred ? preferred : Get(StudentViewClaimTypes.FirstName))} {Get(StudentViewClaimTypes.LastName)}".Trim();
        return View("Default", new StudentViewToolbarModel(name, Get(StudentViewClaimTypes.StudentId) ?? string.Empty));
    }
}

public sealed record StudentViewToolbarModel(string StudentName, string StudentId);
