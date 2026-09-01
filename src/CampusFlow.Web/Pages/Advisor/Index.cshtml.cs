using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Advisor;

[Authorize(CampusFlowPermissions.AdvisorPortal.Default)]
public class IndexModel : CampusFlowPageModel
{
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _termConfigurations;

    public IndexModel(IAdvisorPortalAppService advisorPortal, ITenantThemeProvider tenantThemeProvider,
        IRepository<RegistrationTermConfiguration, Guid> termConfigurations)
    {
        _advisorPortal = advisorPortal;
        _tenantThemeProvider = tenantThemeProvider;
        _termConfigurations = termConfigurations;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public List<AdvisorQueueItemDto> Students { get; private set; } = [];
    public IReadOnlyList<AdvisorTermOption> Terms { get; private set; } = [];
    public AdvisorTermOption? SelectedTerm { get; private set; }

    public async Task OnGetAsync([FromQuery] string? term = null)
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        var configurations = (await _termConfigurations.GetListAsync())
            .Where(x => x.IsStudentSelectable).OrderByDescending(x => x.TermCode).ToArray();
        Terms = configurations.Select(x => new AdvisorTermOption(x.ExternalTermId, x.TermName)).ToArray();
        SelectedTerm = Terms.FirstOrDefault(x => x.ExternalTermId == term)
                       ?? configurations.Where(x => x.IsDashboardDefault)
                           .Select(x => new AdvisorTermOption(x.ExternalTermId, x.TermName)).FirstOrDefault()
                       ?? Terms.FirstOrDefault();
        Students = SelectedTerm is null
            ? []
            : await _advisorPortal.GetQueueAsync(SelectedTerm.ExternalTermId);
    }

    public sealed record AdvisorTermOption(string ExternalTermId, string DisplayName);
}
