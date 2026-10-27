using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using NSubstitute;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;
using Xunit;

namespace CampusFlow.Bibliu;

public class BibliuClientClaimsPrincipalHandlerTests
{
    [Theory]
    [InlineData("client_credentials", "CampusFlow.Bibliu", true, true, true)]
    [InlineData("client_credentials", "CampusFlow.Bibliu", true, false, false)]
    [InlineData("client_credentials", "CampusFlow.Bibliu", false, true, false)]
    [InlineData("client_credentials", "CampusFlow", true, true, false)]
    [InlineData("authorization_code", "CampusFlow.Bibliu", true, true, false)]
    public async Task Only_explicitly_granted_scoped_client_tokens_receive_tenant_binding(
        string grantType, string scope, bool hasTenant, bool granted, bool shouldBind)
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(hasTenant ? tenantId : (Guid?)null);
        var store = Substitute.For<IPermissionStore>();
        store.IsGrantedAsync(CampusFlowPermissions.Admin.BibliuExport, "C", "test").Returns(granted);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        principal.SetScopes(scope);
        var handler = new BibliuClientClaimsPrincipalHandler(store, tenant);
        await handler.HandleAsync(new AbpOpenIddictClaimsPrincipalHandlerContext(
            Substitute.For<IServiceProvider>(),
            new OpenIddictRequest { GrantType = grantType, ClientId = "test" }, principal));
        var claim = principal.FindFirst(AbpClaimTypes.TenantId);
        if (shouldBind)
        {
            claim.ShouldNotBeNull();
            claim.Value.ShouldBe(tenantId.ToString());
            claim.GetDestinations().ShouldContain(OpenIddictConstants.Destinations.AccessToken);
        }
        else claim.ShouldBeNull();
    }
}
