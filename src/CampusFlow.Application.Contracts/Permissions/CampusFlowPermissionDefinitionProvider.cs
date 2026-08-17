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
        var adminPortal = group.AddPermission(CampusFlowPermissions.AdminPortal.Default,
            L("Permission:AdminPortal"), MultiTenancySides.Tenant);
        adminPortal.AddChild(CampusFlowPermissions.AdminPortal.PaymentPlans,
            L("Permission:AdminPortal.PaymentPlans"), MultiTenancySides.Tenant);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CampusFlowResource>(name);
    }
}
