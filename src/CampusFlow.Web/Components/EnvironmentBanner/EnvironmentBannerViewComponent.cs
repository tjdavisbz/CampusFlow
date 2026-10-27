using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Configuration;
using Volo.Abp.AspNetCore.Mvc;

namespace CampusFlow.Web.Components.EnvironmentBanner;

public class EnvironmentBannerViewComponent(IConfiguration configuration) : AbpViewComponent
{
    public IViewComponentResult Invoke() =>
        new HtmlContentViewComponentResult(new HtmlString(PortalEnvironmentBanner.Render(configuration)));
}
