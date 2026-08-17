using System.Threading.Tasks;
using CampusFlow.Localization;
using CampusFlow.Permissions;
using CampusFlow.MultiTenancy;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.UI.Navigation;
using Volo.Abp.TenantManagement.Web.Navigation;
using CampusFlow.Students;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using System;
using Microsoft.Extensions.DependencyInjection;
using CampusFlow.Web.Portals;
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
    private static async Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<CampusFlowResource>();
        var currentUser = context.ServiceProvider.GetRequiredService<ICurrentUser>();
        var profiles = context.ServiceProvider.GetRequiredService<IRepository<StudentProfile, Guid>>();
        var hasStudentProfile = currentUser.Id.HasValue &&
                                await profiles.AnyAsync(x => x.UserId == currentUser.Id.Value);

        if (hasStudentProfile)
        {
            var student = new ApplicationMenuItem(
                CampusFlowMenus.Student,
                "Student",
                icon: "fa fa-user-graduate",
                order: 1
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Home,
                l["Menu:Home"],
                "~/",
                icon: "fa fa-home",
                order: 1
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Schedule,
                "Schedule",
                "~/Schedule",
                icon: "fa fa-calendar-days",
                order: 2
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.CourseSelection,
                "Course Selection",
                "~/CourseSelection",
                icon: "fa fa-list-check",
                order: 3
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.DegreeAudit,
                "Degree Audit",
                "~/DegreeAudit",
                icon: "fa fa-chart-pie",
                order: 4
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Housing,
                "Housing & Meal Plan",
                "~/Housing",
                icon: "fa fa-utensils",
                order: 5
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.Billing,
                "Billing",
                "~/Billing",
                icon: "fa fa-file-invoice-dollar",
                order: 6
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.FinancialAid,
                "Financial Aid",
                "~/FinancialAid",
                icon: "fa fa-graduation-cap",
                order: 7
            )
            );
            student.AddItem(
            new ApplicationMenuItem(
                CampusFlowMenus.BillApproval,
                "Bill Approval",
                "~/BillApproval",
                icon: "fa fa-file-signature",
                order: 8
            )
            );
            context.Menu.AddItem(student);
        }

        if (await context.IsGrantedAsync(CampusFlowPermissions.AdvisorPortal.Default))
        {
            var advisor = new ApplicationMenuItem(
                CampusFlowMenus.Advisor,
                "Advisor",
                icon: "fa fa-user-check",
                order: 2
            );
            advisor.AddItem(new ApplicationMenuItem(
                CampusFlowMenus.AdvisorQueue,
                "Review Queue",
                "~/Advisor",
                icon: "fa fa-list-check",
                order: 1));
            context.Menu.AddItem(advisor);
        }

        var adminAccess = context.ServiceProvider.GetRequiredService<AdminPortalAccessService>();
        if (await adminAccess.EnsureAccessAsync("PaymentPlans"))
        {
            var admin = new ApplicationMenuItem(
                CampusFlowMenus.Admin,
                "Admin",
                icon: "fa fa-user-shield",
                order: 3);
            admin.AddItem(new ApplicationMenuItem(
                CampusFlowMenus.AdminPaymentPlans,
                "Payment Plans",
                "~/Admin/PaymentPlans",
                icon: "fa fa-calendar-check",
                order: 1));
            context.Menu.AddItem(admin);
        }
        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 3;
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

    }
}
