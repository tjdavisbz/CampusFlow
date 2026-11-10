using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using CampusFlow.ClearanceResets;
using CampusFlow.Web.CostTypes;
using CampusFlow.Web.ClearanceResets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace CampusFlow.Pages;
public class AutomaticJobTenantLookupTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task Workers_use_the_framework_normalized_tenant_lookup(bool clearance,bool found)
    {
        var id=Guid.NewGuid();var tenant=Substitute.For<ICurrentTenant>();var store=Substitute.For<ITenantStore>();
        store.FindAsync("NELSON").Returns(Task.FromResult<TenantConfiguration?>(found?new(id,"nelson"):null));
        var services=new ServiceCollection();services.AddSingleton(tenant);services.AddSingleton(store);services.AddSingleton<ITenantNormalizer>(new UpperInvariantTenantNormalizer());
        // No active tenant means the real runner returns before using its collaborators.
        // No database, Elements API, schedule or student is touched by this test.
        services.AddSingleton(new CostTypeAutomationRunner(null!,null!,null!,null!,tenant,null!,null!,null!));
        services.AddSingleton(new ClearanceResetRunner(null!,null!,null!,null!,tenant,null!,null!));
        await using var provider=services.BuildServiceProvider();var scopes=provider.GetRequiredService<IServiceScopeFactory>();
        if(clearance)await new ClearanceResetWorker(scopes,NullLogger<ClearanceResetWorker>.Instance).CheckOnceAsync(CancellationToken.None);
        else await new CostTypeAutomationWorker(scopes,NullLogger<CostTypeAutomationWorker>.Instance).CheckOnceAsync(CancellationToken.None);
        await store.Received(1).FindAsync("NELSON");await store.DidNotReceive().FindAsync("nelson");
        if(found)tenant.Received(1).Change(id);else tenant.DidNotReceive().Change(id);
    }
}
