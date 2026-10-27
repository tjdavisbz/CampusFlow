using System.Threading.Tasks;
using CampusFlow.Permissions;
using OpenIddict.Abstractions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;

namespace CampusFlow.Bibliu;

public sealed class BibliuClientClaimsPrincipalHandler(
    IPermissionStore permissionStore,
    ICurrentTenant currentTenant) : IAbpOpenIddictClaimsPrincipalHandler, ITransientDependency
{
    public async Task HandleAsync(AbpOpenIddictClaimsPrincipalHandlerContext context)
    {
        var request = context.OpenIddictRequest;
        if (!request.IsClientCredentialsGrantType() ||
            !context.Principal.HasScope(BibliuClientPermissionValueProvider.Scope) ||
            currentTenant.Id == null || string.IsNullOrWhiteSpace(request.ClientId))
        {
            return;
        }

        if (await permissionStore.IsGrantedAsync(CampusFlowPermissions.Admin.BibliuExport,
                ClientPermissionValueProvider.ProviderName, request.ClientId))
        {
            // A client has no user identity from which ABP can infer the tenant side.
            // Bind only an explicitly granted BibliU token to its issuing tenant.
            context.Principal.SetClaim(AbpClaimTypes.TenantId, currentTenant.Id.Value.ToString());
            foreach (var claim in context.Principal.FindAll(AbpClaimTypes.TenantId))
                claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
        }
    }
}
