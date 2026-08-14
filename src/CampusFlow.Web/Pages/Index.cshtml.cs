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

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IGuidGenerator guidGenerator,
        ILogger<IndexModel> logger)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _studentLookups = studentLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _guidGenerator = guidGenerator;
        _logger = logger;
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

    public async Task OnGetAsync()
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;

        var termLookup = _termLookups.SingleOrDefault(x =>
            x.Provider == StudentInformationSystemProvider.ThesisElements);
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

        if (CurrentUser.Id is null)
        {
            return;
        }

        var profile = await _studentProfileRepository.FindAsync(x => x.UserId == CurrentUser.Id.Value);
        if (!string.IsNullOrWhiteSpace(CurrentUser.Email))
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
            StudentIdentifier = profile.StudentId;
            StudentDisplayName = profile.DisplayName;
        }
    }
}
