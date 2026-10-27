using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using OpenIddict.Abstractions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;

namespace CampusFlow.Bibliu;

public sealed class BibliuClientPermissionValueProvider(
    IPermissionStore permissionStore,
    ICurrentTenant currentTenant) : PermissionValueProvider(permissionStore)
{
    public const string Scope = "CampusFlow.Bibliu";
    public override string Name => "BibliuTenantClient";

    public override async Task<PermissionGrantResult> CheckAsync(PermissionValueCheckContext context)
    {
        var clientId = context.Principal?.FindFirst(AbpClaimTypes.ClientId)?.Value;
        if (context.Permission.Name != CampusFlowPermissions.Admin.BibliuExport ||
            currentTenant.Id == null || string.IsNullOrWhiteSpace(clientId) ||
            context.Principal?.FindFirst(AbpClaimTypes.TenantId)?.Value != currentTenant.Id.Value.ToString() ||
            context.Principal?.Identity?.IsAuthenticated != true ||
            !context.Principal.HasScope(Scope))
        {
            return PermissionGrantResult.Undefined;
        }

        // ABP's default client provider switches to the host. These export grants
        // deliberately belong to the resolved tenant, like the configuration.
        return await PermissionStore.IsGrantedAsync(context.Permission.Name,
            ClientPermissionValueProvider.ProviderName, clientId)
            ? PermissionGrantResult.Granted
            : PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> CheckAsync(PermissionValuesCheckContext context)
    {
        var result = new MultiplePermissionGrantResult(context.Permissions.Select(p => p.Name).ToArray());
        foreach (var permission in context.Permissions)
        {
            result.Result[permission.Name] = await CheckAsync(new PermissionValueCheckContext(permission, context.Principal));
        }
        return result;
    }
}
