using CampusFlow.Branding;
using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web;

[Dependency(ReplaceServices = true)]
public class CampusFlowBrandingProvider : DefaultBrandingProvider
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public CampusFlowBrandingProvider(
        ICurrentTenant currentTenant,
        ITenantThemeProvider tenantThemeProvider)
    {
        _currentTenant = currentTenant;
        _tenantThemeProvider = tenantThemeProvider;
    }

    private TenantTheme Theme => _tenantThemeProvider.Get(_currentTenant.Name);

    public override string AppName => Theme.OrganizationName;
    public override string? LogoUrl => Theme.LogoUrl;
    public override string? LogoReverseUrl => Theme.LogoReverseUrl;
}
