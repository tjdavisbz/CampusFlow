using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }
    public string InstitutionName => TenantName?.Equals("nelson", System.StringComparison.OrdinalIgnoreCase) == true
        ? "Nelson University"
        : TenantName ?? "Your University";
    public string StudentIdentifier { get; private set; } = "Unavailable";

    public void OnGet()
    {
        TenantName = CurrentTenant.Name;
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;
        const string prefix = "student-";
        var userName = CurrentUser.UserName;
        StudentIdentifier = userName?.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) == true
            ? userName[prefix.Length..]
            : "Unavailable";
    }
}
