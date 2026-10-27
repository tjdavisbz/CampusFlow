using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using NSubstitute;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace CampusFlow.Bibliu;

public class BibliuClientPermissionValueProviderTests
{
    private sealed class TestPermission(string name) : PermissionDefinition(name);

    private readonly Guid _allowedTenant = Guid.NewGuid();
    private readonly IPermissionStore _store = Substitute.For<IPermissionStore>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly BibliuClientPermissionValueProvider _provider;

    public BibliuClientPermissionValueProviderTests()
    {
        _tenant.Id.Returns(_allowedTenant);
        _store.IsGrantedAsync(CampusFlowPermissions.Admin.BibliuExport, "C", "bibliu-test")
            .Returns(_ => _tenant.Id == _allowedTenant);
        _provider = new BibliuClientPermissionValueProvider(_store, _tenant);
    }

    private ClaimsPrincipal Client(string? scope = BibliuClientPermissionValueProvider.Scope,
        bool authenticated = true, bool includeClientId = true)
    {
        var identity = new ClaimsIdentity(authenticated ? "Bearer" : null);
        if (includeClientId) identity.AddClaim(new Claim(AbpClaimTypes.ClientId, "bibliu-test"));
        identity.AddClaim(new Claim(AbpClaimTypes.TenantId, _allowedTenant.ToString()));
        var principal = new ClaimsPrincipal(identity);
        if (scope != null) principal.SetScopes(scope);
        return principal;
    }

    private Task<PermissionGrantResult> Check(ClaimsPrincipal principal,
        string permission = CampusFlowPermissions.Admin.BibliuExport) =>
        _provider.CheckAsync(new PermissionValueCheckContext(new TestPermission(permission), principal));

    [Fact]
    public async Task Tenant_grant_allows_scoped_authenticated_client()
    {
        (await Check(Client())).ShouldBe(PermissionGrantResult.Granted);
        await _store.Received(1).IsGrantedAsync(CampusFlowPermissions.Admin.BibliuExport, "C", "bibliu-test");
        _tenant.DidNotReceiveWithAnyArgs().Change(default);
    }

    [Fact]
    public async Task Grant_does_not_follow_client_into_another_tenant()
    {
        _tenant.Id.Returns(Guid.NewGuid());
        (await Check(Client())).ShouldBe(PermissionGrantResult.Undefined);
    }

    [Fact]
    public async Task Host_cannot_use_tenant_grant()
    {
        _tenant.Id.Returns((Guid?)null);
        (await Check(Client())).ShouldBe(PermissionGrantResult.Undefined);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CampusFlow")]
    public async Task Missing_or_different_scope_cannot_use_grant(string? scope)
    {
        (await Check(Client(scope))).ShouldBe(PermissionGrantResult.Undefined);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Anonymous_or_nonclient_principal_cannot_use_grant(bool authenticated, bool includeClientId)
    {
        (await Check(Client(authenticated: authenticated, includeClientId: includeClientId)))
            .ShouldBe(PermissionGrantResult.Undefined);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_grant_never_grants_settings_editing()
    {
        (await Check(Client(), CampusFlowPermissions.Admin.BibliuConfiguration))
            .ShouldBe(PermissionGrantResult.Undefined);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Batch_check_keeps_other_permissions_undefined()
    {
        var result = await _provider.CheckAsync(new PermissionValuesCheckContext(new List<PermissionDefinition>
        {
            new TestPermission(CampusFlowPermissions.Admin.BibliuExport),
            new TestPermission(CampusFlowPermissions.Admin.BibliuConfiguration)
        }, Client()));
        result.Result[CampusFlowPermissions.Admin.BibliuExport].ShouldBe(PermissionGrantResult.Granted);
        result.Result[CampusFlowPermissions.Admin.BibliuConfiguration].ShouldBe(PermissionGrantResult.Undefined);
    }
}
