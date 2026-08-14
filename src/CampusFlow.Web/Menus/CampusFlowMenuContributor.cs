using System.Threading.Tasks;
using CampusFlow.Localization;
using CampusFlow.Permissions;
using CampusFlow.MultiTenancy;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.UI.Navigation;
using Volo.Abp.TenantManagement.Web.Navigation;
namespace CampusFlow.Web.Menus;
public class CampusFlowMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }
    private static Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<CampusFlowResource>();
        //Home
        context.Menu.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Home,
                l["Menu:Home"],
                "~/",
                icon: "fa fa-home",
                order: 1
            )
        );
        context.Menu.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Schedule,
                "Schedule",
                "~/Schedule",
                icon: "fa fa-calendar-days",
                order: 2
            )
        );
        context.Menu.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Billing,
                "Billing",
                "~/Billing",
                icon: "fa fa-file-invoice-dollar",
                order: 3
            )
        );
        context.Menu.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.FinancialAid,
                "Financial Aid",
                "~/FinancialAid",
                icon: "fa fa-graduation-cap",
                order: 4
            )
        );
        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 6;
        //Administration->Identity
        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 1);

        if (MultiTenancyConsts.IsEnabled)
        {
            administration.SetSubItemOrder(TenantManagementMenuNames.GroupName, 1);
        }
        else
        {
            administration.TryRemoveMenuItem(TenantManagementMenuNames.GroupName);
        }

        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 3);
        //Administration->Settings
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 8);

        return Task.CompletedTask;
    }
}
