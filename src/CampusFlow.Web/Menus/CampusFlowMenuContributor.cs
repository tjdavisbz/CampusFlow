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
        var currentStudentView = context.ServiceProvider.GetRequiredService<ICurrentStudentView>();
        var hasStudentProfile = await currentStudentView.GetProfileAsync() is not null;

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
                CampusFlowMenus.DegreeAudit,
                "Degree Audit",
                "~/DegreeAudit",
                icon: "fa fa-chart-pie",
                order: 4
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

        var impersonationAccess = context.ServiceProvider.GetRequiredService<StudentImpersonationAccessService>();
        var canImpersonate = await impersonationAccess.EnsureAccessAsync();
        var canManagePlans = await context.IsGrantedAsync(CampusFlowPermissions.Admin.PaymentPlans);
        var canManageGlobalConfiguration = await context.IsGrantedAsync(CampusFlowPermissions.Admin.GlobalConfiguration);
        var canManageBillApproval = await context.IsGrantedAsync(CampusFlowPermissions.Admin.BillApproval);
        var canManageRegistration = await context.IsGrantedAsync(CampusFlowPermissions.Admin.RegistrationRules);
        var canManageAdvisorRouting = await context.IsGrantedAsync(CampusFlowPermissions.AdvisorPortal.ManageRouting);
        var canManageAccess = await context.IsGrantedAsync(CampusFlowPermissions.Admin.AccessManagement);
        if (canImpersonate || canManagePlans || canManageGlobalConfiguration || canManageBillApproval || canManageRegistration || canManageAdvisorRouting || canManageAccess)
        {
            var admin = new ApplicationMenuItem(
                CampusFlowMenus.Admin, "Admin", icon: "fa fa-user-shield", order: 3);
            if (canManageGlobalConfiguration)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.GlobalConfiguration, "Global Configuration",
                    "~/Admin/GlobalConfiguration", icon: "fa fa-globe", order: 1));
            if (canManagePlans)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.PaymentPlans, "Payment Plans",
                    "~/Admin/PaymentPlans", icon: "fa fa-credit-card", order: 1));
            if (canManageBillApproval)
            {
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.BillApprovalConfiguration, "Bill Approval",
                    "~/Admin/BillApproval", icon: "fa fa-file-signature", order: 2));
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.Agreements, "Agreements",
                    "~/Admin/Agreements", icon: "fa fa-file-contract", order: 3));
            }
            if (canManageRegistration)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.RegistrationRules, "Course Selection",
                    "~/Admin/CourseSelection", icon: "fa fa-list-check", order: 4));
            if (canManageAdvisorRouting)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.AdvisorVisibility, "Advisor Visibility",
                    "~/Admin/AdvisorVisibility", icon: "fa fa-people-arrows", order: 5));
            if (canImpersonate)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.ImpersonateStudent, "Impersonate Student",
                    "~/Admin/ImpersonateStudent", icon: "fa fa-user-magnifying-glass", order: 3));
            if (canManageAccess)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.AccessManagement, "Users & Roles",
                    "~/Identity/Users", icon: "fa fa-users-gear", order: 4));
            context.Menu.AddItem(admin);
        }
        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 4;
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
