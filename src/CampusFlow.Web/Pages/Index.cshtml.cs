using CampusFlow.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IGuidGenerator _guidGenerator;

    public IndexModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IGuidGenerator guidGenerator)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _studentLookups = studentLookups.ToArray();
        _guidGenerator = guidGenerator;
    }

    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string InstitutionName => Theme.OrganizationName;
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public string StudentDisplayName { get; private set; } = "Student";

    public async Task OnGetAsync()
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;
        if (CurrentUser.Id is null)
        {
            return;
        }

        var profile = await _studentProfileRepository.FindAsync(x => x.UserId == CurrentUser.Id.Value);
        if (profile is null && !string.IsNullOrWhiteSpace(CurrentUser.Email))
        {
            var lookup = _studentLookups.SingleOrDefault(x =>
                x.Provider == StudentInformationSystemProvider.ThesisElements);
            var result = lookup is null
                ? null
                : await lookup.FindByEmailAsync(CurrentUser.Email, HttpContext.RequestAborted);

            if (result?.Status == StudentLookupStatus.Matched && result.Student is not null)
            {
                profile = await _studentProfileRepository.InsertAsync(new StudentProfile(
                    _guidGenerator.Create(),
                    CurrentTenant.Id,
                    CurrentUser.Id.Value,
                    result.Student));
            }
        }

        if (profile is not null)
        {
            StudentIdentifier = profile.StudentId;
            StudentDisplayName = profile.DisplayName;
        }
    }
}
