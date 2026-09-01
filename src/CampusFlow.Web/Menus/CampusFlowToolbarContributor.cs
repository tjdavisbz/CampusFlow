using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared.Toolbars;
using Volo.Abp.Users;
using CampusFlow.Web.Components.Toolbar.LoginLink;
using CampusFlow.Students;
using CampusFlow.Web.Components.Toolbar.StudentView;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace CampusFlow.Web.Menus;

public class CampusFlowToolbarContributor : IToolbarContributor
{
    public virtual Task ConfigureToolbarAsync(IToolbarConfigurationContext context)
    {
        if (context.Toolbar.Name != StandardToolbars.Main)
        {
            return Task.CompletedTask;
        }

        if (!context.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated)
        {
            context.Toolbar.Items.Add(new ToolbarItem(typeof(LoginLinkViewComponent)));
        }

        var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext?.User.Claims.Any(x => x.Type == StudentViewClaimTypes.ExternalStudentId) == true)
        {
            context.Toolbar.Items.Add(new ToolbarItem(typeof(StudentViewViewComponent), order: -100));
        }
		
        return Task.CompletedTask;
    }
}
