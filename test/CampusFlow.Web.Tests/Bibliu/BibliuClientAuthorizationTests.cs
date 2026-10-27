using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using OpenIddict.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace CampusFlow.Bibliu;

public class BibliuClientAuthorizationTests : CampusFlowWebTestBase
{
    [Fact]
    public async Task Registered_provider_honors_only_its_tenant_export_grant()
    {
        var tenant = GetRequiredService<ICurrentTenant>();
        // The shared test module installs an always-allow checker; exercise the real one here.
        var checker = ActivatorUtilities.CreateInstance<PermissionChecker>(GetRequiredService<IServiceProvider>());
        var tenantId = Guid.NewGuid();
        var clientId = "bibliu-test-" + Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.ClientId, clientId)
        }, "Bearer"));
        principal.SetScopes(BibliuClientPermissionValueProvider.Scope);

        using (tenant.Change(tenantId))
        {
            using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
            {
                await GetRequiredService<IPermissionDataSeeder>().SeedAsync("C", clientId,
                    new[] { CampusFlowPermissions.Admin.BibliuExport }, tenantId);
                await uow.CompleteAsync();
            }
            await GetRequiredService<AbpOpenIddictClaimsPrincipalManager>().HandleAsync(
                new OpenIddictRequest
                {
                    GrantType = OpenIddictConstants.GrantTypes.ClientCredentials,
                    ClientId = clientId
                }, principal);
            principal.FindFirst(AbpClaimTypes.TenantId)?.Value.ShouldBe(tenantId.ToString());
            (await checker.IsGrantedAsync(principal, CampusFlowPermissions.Admin.BibliuExport)).ShouldBeTrue();
            (await checker.IsGrantedAsync(principal, CampusFlowPermissions.Admin.BibliuConfiguration)).ShouldBeFalse();
            principal.SetScopes("CampusFlow");
            (await checker.IsGrantedAsync(principal, CampusFlowPermissions.Admin.BibliuExport)).ShouldBeFalse();
        }

        principal.SetScopes(BibliuClientPermissionValueProvider.Scope);
        using (tenant.Change(Guid.NewGuid()))
            (await checker.IsGrantedAsync(principal, CampusFlowPermissions.Admin.BibliuExport)).ShouldBeFalse();
        using (tenant.Change(null))
            (await checker.IsGrantedAsync(principal, CampusFlowPermissions.Admin.BibliuExport)).ShouldBeFalse();
    }
}
