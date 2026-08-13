using CampusFlow.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public IndexModel(ITenantThemeProvider tenantThemeProvider)
    {
        _tenantThemeProvider = tenantThemeProvider;
    }

    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033", null, null);
    public string InstitutionName => Theme.OrganizationName;
    public string StudentIdentifier { get; private set; } = "Unavailable";

    public void OnGet()
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;
        const string prefix = "student-";
        var userName = CurrentUser.UserName;
        StudentIdentifier = userName?.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) == true
            ? userName[prefix.Length..]
            : "Unavailable";
    }
}
