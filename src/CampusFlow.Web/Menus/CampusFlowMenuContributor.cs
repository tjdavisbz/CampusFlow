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
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var facultyReader = context.ServiceProvider.GetRequiredService<FacultyPortalReader>();
        var studentProfile = await currentStudentView.GetProfileAsync();
        var hasStudentProfile = studentProfile is not null;

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
            if (!currentStudentView.IsImpersonating &&
                studentProfile?.Provider == CampusFlow.StudentInformationSystems.StudentInformationSystemProvider.ThesisElements &&
                string.Equals(context.ServiceProvider.GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Name,
                    "nelson", StringComparison.OrdinalIgnoreCase))
            {
                student.AddItem(new ApplicationMenuItem(
                    CampusFlowMenus.PrivacyPasswords,
                    "FERPA / Privacy Passwords",
                    "~/PrivacyPasswords",
                    icon: "fa fa-lock",
                    order: 8));
            }
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

        var hasFacultyAccess = false;
        var httpContext = context.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext;
        var viewedFacultyId = httpContext is null ? null : FacultyViewSession.GetFacultyId(httpContext);
        if (!currentStudentView.IsImpersonating && currentUser.IsAuthenticated)
        {
            try
            {
                hasFacultyAccess = viewedFacultyId.HasValue
                    ? await facultyReader.FindByIdAsync(viewedFacultyId.Value, default) is not null
                    : await facultyReader.FindFacultyAsync(currentUser.Email, default) is not null;
            }
            catch (Exception exception)
            {
                context.ServiceProvider.GetRequiredService<ILogger<CampusFlowMenuContributor>>()
                    .LogWarning(exception, "Unable to check faculty portal access for the menu.");
            }
        }
        if (hasFacultyAccess)
        {
            var faculty = new ApplicationMenuItem("CampusFlow.Faculty", "Faculty", icon: "fa fa-chalkboard-user", order: 2);
            faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.home", "Home", "~/Faculty", icon: "fa fa-home", order: 1));
            faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.students", "My Students", "~/Faculty/students", icon: "fa fa-users", order: 2));
            faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.schedule", "My Schedule", "~/Faculty/schedule", icon: "fa fa-calendar-days", order: 3));
            faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.roster", "Class Roster", "~/Faculty/roster", icon: "fa fa-list", order: 4));
            faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.courses", "My Courses", "~/Faculty/courses", icon: "fa fa-book", order: 5));
            if (!viewedFacultyId.HasValue)
                faculty.AddItem(new ApplicationMenuItem("CampusFlow.Faculty.email", "Email Students", "~/Faculty/email", icon: "fa fa-envelope", order: 6));
            context.Menu.AddItem(faculty);
        }

        var canImpersonate = await context.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default);
        var canManagePlans = await context.IsGrantedAsync(CampusFlowPermissions.Admin.PaymentPlans);
        var canManageGlobalConfiguration = await context.IsGrantedAsync(CampusFlowPermissions.Admin.GlobalConfiguration);
        var canManageBillApproval = await context.IsGrantedAsync(CampusFlowPermissions.Admin.BillApproval);
        var canManageRegistration = await context.IsGrantedAsync(CampusFlowPermissions.Admin.RegistrationRules);
        var canManageAdvisorRouting = await context.IsGrantedAsync(CampusFlowPermissions.AdvisorPortal.ManageRouting);
        var canResetBillApproval = await context.IsGrantedAsync(CampusFlowPermissions.Admin.ResetIndividualBillApproval);
        var canAddStudentMealPlan = await context.IsGrantedAsync(CampusFlowPermissions.Admin.AddStudentMealPlan);
        var canManageHousing = await context.IsGrantedAsync(CampusFlowPermissions.Admin.HousingAssignments);
        if (canImpersonate || canManagePlans || canManageGlobalConfiguration || canManageBillApproval || canManageRegistration || canManageAdvisorRouting || canResetBillApproval || canAddStudentMealPlan || canManageHousing)
        {
            var admin = new ApplicationMenuItem(
                CampusFlowMenus.Admin, "Admin", icon: "fa fa-user-shield", order: 3);
            if (canManageGlobalConfiguration)
            {
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.GlobalConfiguration, "Global Configuration",
                    "~/Admin/GlobalConfiguration", icon: "fa fa-globe", order: 1));
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.Announcements, "Announcements",
                    "~/Admin/Announcements", icon: "fa fa-bullhorn", order: 2));
            }
            var studentBilling = new ApplicationMenuItem(CampusFlowMenus.StudentBillingAdmin, "Student Billing",
                icon: "fa fa-file-invoice-dollar", order: 4);
            if (canManagePlans)
                studentBilling.AddItem(new ApplicationMenuItem(CampusFlowMenus.PaymentPlans, "Payment Plans",
                    "~/Admin/PaymentPlans", icon: "fa fa-credit-card", order: 2));
            if (canManageBillApproval)
            {
                studentBilling.AddItem(new ApplicationMenuItem(CampusFlowMenus.BillApprovalConfiguration, "Bill Approval",
                    "~/Admin/BillApproval", icon: "fa fa-file-signature", order: 1));
                studentBilling.AddItem(new ApplicationMenuItem(CampusFlowMenus.Agreements, "Agreements",
                    "~/Admin/Agreements", icon: "fa fa-file-contract", order: 3));
            }
            if (canResetBillApproval)
                studentBilling.AddItem(new ApplicationMenuItem(CampusFlowMenus.ResetIndividualBillApproval,
                    "Reset Bill Approval", "~/Admin/StudentBilling/ResetIndividualBillApproval",
                    icon: "fa fa-arrow-rotate-left", order: 4));
            if (studentBilling.Items.Count > 0)
                admin.AddItem(studentBilling);
            if (canAddStudentMealPlan)
            {
                var businessServices = new ApplicationMenuItem(CampusFlowMenus.BusinessServicesAdmin,
                    "Business Services", icon: "fa fa-briefcase", order: 5);
                businessServices.AddItem(new ApplicationMenuItem(CampusFlowMenus.AddStudentMealPlan,
                    "Add Student Meal Plan", "~/Admin/BusinessServices/AddStudentMealPlan",
                    icon: "fa fa-utensils", order: 1));
                admin.AddItem(businessServices);
            }
            if (canManageRegistration)
            {
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.RegistrationRules, "Course Selection",
                    "~/Admin/CourseSelection", icon: "fa fa-list-check", order: 2));
            }
            if (canManageHousing)
                admin.AddItem(new ApplicationMenuItem("CampusFlow.HousingAdmin", "Housing", icon: "fa fa-building", order: 6)
                    .AddItem(new ApplicationMenuItem("CampusFlow.HousingAssignments", "Assignments", "~/Admin/Housing/Assignments", icon: "fa fa-bed")));
            if (canManageAdvisorRouting)
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.AdvisorVisibility, "Advisor Assignments",
                    "~/Admin/AdvisorVisibility", icon: "fa fa-people-arrows", order: 3));
            if (canImpersonate)
            {
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.ImpersonateStudent, "Impersonate Student",
                    "~/Admin/ImpersonateStudent", icon: "fa fa-user-secret", order: 7));
                admin.AddItem(new ApplicationMenuItem(CampusFlowMenus.ImpersonateFaculty, "Impersonate Faculty",
                    "~/Admin/ImpersonateFaculty", icon: "fa fa-chalkboard-user", order: 8));
            }
            context.Menu.AddItem(admin);
        }
        //Administration
        var administration = context.Menu.GetAdministration();
        administration.Order = 4;
        //Administration->Identity
        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 1);
        var identityManagement = administration.Items.FirstOrDefault(x => x.Name == IdentityMenuNames.GroupName);
        if (identityManagement is not null)
            identityManagement.DisplayName = "Users & Access";

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
