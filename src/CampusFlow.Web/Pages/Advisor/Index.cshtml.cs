using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace CampusFlow.Web.Pages.Advisor;

[Authorize(CampusFlowPermissions.AdvisorPortal.Default)]
public class IndexModel : CampusFlowPageModel
{
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public IndexModel(IAdvisorPortalAppService advisorPortal, ITenantThemeProvider tenantThemeProvider)
    {
        _advisorPortal = advisorPortal;
        _tenantThemeProvider = tenantThemeProvider;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public List<AdvisorQueueItemDto> Students { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        Students = await _advisorPortal.GetQueueAsync();
    }
}
