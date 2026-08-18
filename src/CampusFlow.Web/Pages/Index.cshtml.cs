using CampusFlow.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using CampusFlow.AdvisorPortal;
using CampusFlow.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly IdentityUserManager _userManager;

    public IndexModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IGuidGenerator guidGenerator,
        ILogger<IndexModel> logger,
        IAdvisorPortalAppService advisorPortal,
        IPermissionChecker permissionChecker,
        IStudentInformationSystemAdvisorLookup advisorLookup,
        IdentityUserManager userManager)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _currentStudentView = currentStudentView;
        _studentLookups = studentLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _guidGenerator = guidGenerator;
        _logger = logger;
        _advisorPortal = advisorPortal;
        _permissionChecker = permissionChecker;
        _advisorLookup = advisorLookup;
        _userManager = userManager;
    }

    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string InstitutionName => Theme.OrganizationName;
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public string StudentDisplayName { get; private set; } = "Student";
    public StudentInformationSystemTerm? CurrentTerm { get; private set; }
    public bool HasStudentAccess { get; private set; }
    public bool HasAdvisorAccess { get; private set; }
    public int AdvisorStudentCount { get; private set; }
    public int AdvisorCourseCount { get; private set; }

    public async Task OnGetAsync()
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;

        if (CurrentUser.Id is null)
        {
            return;
        }

        HasAdvisorAccess = !_currentStudentView.IsImpersonating && await _permissionChecker.IsGrantedAsync(
            CampusFlowPermissions.AdvisorPortal.Default);

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (!_currentStudentView.IsImpersonating && !string.IsNullOrWhiteSpace(CurrentUser.Email))
        {
            var lookup = _studentLookups.SingleOrDefault(x =>
                x.Provider == (profile?.Provider ?? StudentInformationSystemProvider.ThesisElements));
            var result = lookup is null
                ? null
                : await lookup.FindByEmailAsync(CurrentUser.Email, HttpContext.RequestAborted);

            if (result?.Status == StudentLookupStatus.Matched && result.Student is not null)
            {
                if (profile is null)
                {
                    profile = await _studentProfileRepository.InsertAsync(new StudentProfile(
                        _guidGenerator.Create(),
                        CurrentTenant.Id,
                        CurrentUser.Id.Value,
                        result.Student));
                }
                else if (profile.Provider == result.Student.Provider &&
                         profile.ExternalStudentId == result.Student.ExternalStudentId)
                {
                    profile.Update(result.Student);
                    profile = await _studentProfileRepository.UpdateAsync(profile);
                }
            }
        }

        if (profile is not null)
        {
            HasStudentAccess = true;
            StudentIdentifier = profile.StudentId;
            StudentDisplayName = profile.DisplayName;

            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (termLookup is not null)
            {
                try
                {
                    CurrentTerm = await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Unable to resolve the current academic term.");
                }
            }
        }
        else
        {
            StudentDisplayName = CurrentUser.Name ?? CurrentUser.UserName ?? "Advisor";
        }

        if (HasAdvisorAccess)
        {
            if (!string.IsNullOrWhiteSpace(CurrentUser.Email))
            {
                try
                {
                    var advisor = await _advisorLookup.FindAsync(
                        CurrentUser.Email, HttpContext.RequestAborted);
                    if (advisor is not null)
                    {
                        StudentDisplayName = advisor.DisplayName;
                        var user = await _userManager.GetByIdAsync(CurrentUser.Id.Value);
                        if (!string.Equals(user.Name, advisor.FirstName, StringComparison.Ordinal) ||
                            !string.Equals(user.Surname, advisor.LastName, StringComparison.Ordinal))
                        {
                            user.Name = advisor.FirstName;
                            user.Surname = advisor.LastName;
                            var update = await _userManager.UpdateAsync(user);
                            if (!update.Succeeded)
                            {
                                _logger.LogWarning("Unable to refresh the advisor's display name from Elements.");
                            }
                        }
                    }
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Unable to refresh the advisor identity from Elements.");
                }
            }

            var queue = await _advisorPortal.GetQueueAsync();
            AdvisorStudentCount = queue.Count;
            AdvisorCourseCount = queue.Sum(x => x.PendingCourseCount);
        }
    }
}
