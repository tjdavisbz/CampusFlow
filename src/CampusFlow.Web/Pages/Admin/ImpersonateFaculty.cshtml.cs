using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class ImpersonateFacultyModel(
    FacultyPortalReader facultyReader,
    FacultyViewSession session,
    IPermissionChecker permissionChecker,
    ILogger<ImpersonateFacultyModel> logger,
    ITenantThemeProvider tenantThemeProvider) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)]
    public string Query { get; set; } = string.Empty;

    [BindProperty]
    public int FacultyId { get; set; }

    public IReadOnlyList<FacultySearchResult> Results { get; private set; } = [];
    public bool SearchPerformed { get; private set; }
    public string? SearchError { get; private set; }
    public TenantTheme Theme { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanUseAsync()) return Forbid();
        Theme = tenantThemeProvider.Get(CurrentTenant.Name);
        if (string.IsNullOrWhiteSpace(Query) || Query.Trim().Length < 2) return Page();

        SearchPerformed = true;
        try
        {
            Results = await facultyReader.SearchAsync(Query.Trim(), HttpContext.RequestAborted);
            logger.LogInformation("Faculty impersonation search returned {ResultCount} records. TraceId={TraceId}",
                Results.Count, HttpContext.TraceIdentifier);
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            SearchError = "The faculty search took too long. Please try again.";
            logger.LogWarning(exception, "Faculty impersonation search timed out. TraceId={TraceId}",
                HttpContext.TraceIdentifier);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync()
    {
        if (!await CanUseAsync()) return Forbid();
        if (FacultyId <= 0) return BadRequest();
        var faculty = await facultyReader.FindByIdAsync(FacultyId, HttpContext.RequestAborted);
        if (faculty is null) return NotFound();

        session.Start(HttpContext, faculty);
        logger.LogWarning("Administrator {ActorUserId} started read-only faculty view for {FacultyId}. TraceId={TraceId}",
            CurrentUser.Id, faculty.FacultyId, HttpContext.TraceIdentifier);
        return Redirect("/Faculty");
    }

    private Task<bool> CanUseAsync() =>
        permissionChecker.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default);
}
