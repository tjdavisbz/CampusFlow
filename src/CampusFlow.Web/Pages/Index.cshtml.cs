using Microsoft.AspNetCore.Mvc;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.Pages;

public class IndexModel : CampusFlowPageModel
{
    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }

    public void OnGet()
    {
        TenantName = CurrentTenant.Name;
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;
    }
}
