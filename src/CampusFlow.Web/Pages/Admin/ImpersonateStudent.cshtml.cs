using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class ImpersonateStudentModel : CampusFlowPageModel
{
    private readonly StudentImpersonationAccessService _access;
    private readonly StudentViewSession _session;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _lookups;
    private readonly ILogger<ImpersonateStudentModel> _logger;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public ImpersonateStudentModel(
        StudentImpersonationAccessService access,
        StudentViewSession session,
        IPermissionChecker permissionChecker,
        IEnumerable<IStudentInformationSystemStudentLookup> lookups,
        ILogger<ImpersonateStudentModel> logger,
        ITenantThemeProvider tenantThemeProvider)
    {
        _access = access;
        _session = session;
        _permissionChecker = permissionChecker;
        _lookups = lookups.ToArray();
        _logger = logger;
        _tenantThemeProvider = tenantThemeProvider;
    }

    [BindProperty(SupportsGet = true)]
    public string Query { get; set; } = string.Empty;

    [BindProperty]
    public string ExternalStudentId { get; set; } = string.Empty;

    public IReadOnlyList<StudentInformationSystemStudent> Results { get; private set; } = [];
    public bool SearchPerformed { get; private set; }
    public TenantTheme Theme { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanUseAsync()) return Forbid();
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        if (string.IsNullOrWhiteSpace(Query) || Query.Trim().Length < 2) return Page();

        SearchPerformed = true;
        var lookup = _lookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        Results = lookup is null
            ? []
            : await lookup.SearchAsync(Query.Trim(), cancellationToken: HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync()
    {
        if (!await CanUseAsync()) return Forbid();
        var lookup = _lookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (lookup is null || string.IsNullOrWhiteSpace(ExternalStudentId)) return BadRequest();

        var matches = await lookup.SearchAsync(ExternalStudentId.Trim(), 10, HttpContext.RequestAborted);
        var student = matches.SingleOrDefault(x =>
            string.Equals(x.ExternalStudentId, ExternalStudentId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (student is null) return NotFound();

        _session.Start(HttpContext, student);
        _logger.LogWarning(
            "Administrator {ActorUserId} started read-only student view for {ExternalStudentId} ({StudentId}). TraceId={TraceId}",
            CurrentUser.Id, student.ExternalStudentId, student.StudentId, HttpContext.TraceIdentifier);
        return RedirectToPage("/Index");
    }

    private async Task<bool> CanUseAsync() =>
        await _access.EnsureAccessAsync() &&
        await _permissionChecker.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default);
}
