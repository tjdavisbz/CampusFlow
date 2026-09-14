using CampusFlow.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Permissions;

public class CampusFlowPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CampusFlowPermissions.GroupName);
        var advisorPortal = group.AddPermission(CampusFlowPermissions.AdvisorPortal.Default,
            L("Permission:AdvisorPortal"), MultiTenancySides.Tenant);
        advisorPortal.AddChild(CampusFlowPermissions.AdvisorPortal.ViewAll,
            L("Permission:AdvisorPortal.ViewAll"), MultiTenancySides.Tenant);
        advisorPortal.AddChild(CampusFlowPermissions.AdvisorPortal.ManageRouting,
            L("Permission:AdvisorPortal.ManageRouting"), MultiTenancySides.Tenant);
        group.AddPermission(CampusFlowPermissions.StudentImpersonation.Default,
            L("Permission:StudentImpersonation"), MultiTenancySides.Tenant);
        var admin = group.AddPermission(CampusFlowPermissions.Admin.Default,
            L("Permission:Admin"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.GlobalConfiguration,
            L("Permission:Admin.GlobalConfiguration"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.PaymentPlans,
            L("Permission:Admin.PaymentPlans"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.BillApproval,
            L("Permission:Admin.BillApproval"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.RegistrationRules,
            L("Permission:Admin.RegistrationRules"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.AccessManagement,
            L("Permission:Admin.AccessManagement"), MultiTenancySides.Tenant);
        var studentBilling = admin.AddChild(CampusFlowPermissions.Admin.StudentBilling,
            L("Permission:Admin.StudentBilling"), MultiTenancySides.Tenant);
        studentBilling.AddChild(CampusFlowPermissions.Admin.ResetIndividualBillApproval,
            L("Permission:Admin.StudentBilling.ResetIndividualBillApproval"), MultiTenancySides.Tenant);
        var businessServices = admin.AddChild(CampusFlowPermissions.Admin.BusinessServices,
            L("Permission:Admin.BusinessServices"), MultiTenancySides.Tenant);
        admin.AddChild(CampusFlowPermissions.Admin.HousingAssignments,
            new Volo.Abp.Localization.FixedLocalizableString("Manage housing assignment drafts"), MultiTenancySides.Tenant);
        businessServices.AddChild(CampusFlowPermissions.Admin.AddStudentMealPlan,
            L("Permission:Admin.BusinessServices.AddStudentMealPlan"), MultiTenancySides.Tenant);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CampusFlowResource>(name);
    }
}
