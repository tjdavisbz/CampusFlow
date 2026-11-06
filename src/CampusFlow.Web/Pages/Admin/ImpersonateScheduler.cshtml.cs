using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.SchedulerImpersonation.Default)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ImpersonateSchedulerModel(IStudentInformationSystemAdvisorLookup lookup,
    IRepository<AdvisorAssignment, Guid> assignments, SchedulerViewSession session,
    IPermissionChecker permissions, ILogger<ImpersonateSchedulerModel> logger,
    ITenantThemeProvider themes) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public string Query { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    public IReadOnlyList<AdvisorLookupResult> Results { get; private set; } = [];
    public bool SearchPerformed { get; private set; }
    public string? SearchError { get; private set; }
    public TenantTheme Theme { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default)) return Forbid();
        Theme = themes.Get(CurrentTenant.Name);
        if (string.IsNullOrWhiteSpace(Query) || Query.Trim().Length < 2) return Page();
        if (Query.Length > 100) return BadRequest();
        SearchPerformed = true;
        try
        {
            var active = await ActiveAsync();
            Results = (await lookup.SearchAsync(Query.Trim(), HttpContext.RequestAborted))
                .Where(x => active.Any(a => string.Equals(a.AdvisorEmail, x.Email, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            SearchError = "The scheduler search is unavailable. Please try again shortly.";
            logger.LogWarning(exception, "Scheduler impersonation search failed. TraceId={TraceId}", HttpContext.TraceIdentifier);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync()
    {
        if (!await permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default)) return Forbid();
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 254) return BadRequest();
        if (HttpContext.User.HasClaim(x => x.Type == StudentViewClaimTypes.ExternalStudentId || x.Type == FacultyViewClaimTypes.FacultyId)) return Forbid();
        var scheduler = await lookup.FindAsync(Email.Trim(), HttpContext.RequestAborted);
        if (scheduler is null || !(await ActiveAsync()).Any(x => string.Equals(x.AdvisorEmail, scheduler.Email, StringComparison.OrdinalIgnoreCase))) return NotFound();
        session.Start(HttpContext, scheduler);
        logger.LogWarning("Administrator {ActorUserId} started read-only scheduler view for {SchedulerEmail}. TraceId={TraceId}", CurrentUser.Id, scheduler.Email, HttpContext.TraceIdentifier);
        return Redirect("/Advisor");
    }

    private Task<List<AdvisorAssignment>> ActiveAsync()
    {
        var now = Clock.Now;
        return assignments.GetListAsync(x => x.IsActive && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now));
    }
}
