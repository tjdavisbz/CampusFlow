using CampusFlow.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Permissions;

public class CampusFlowPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(CampusFlowPermissions.GroupName);

        //Define your own permissions here. Example:
        //myGroup.AddPermission(CampusFlowPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CampusFlowResource>(name);
    }
}
