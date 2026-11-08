using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.CostTypes;

public sealed class CostTypeAutomationWorker(IServiceScopeFactory scopes,ILogger<CostTypeAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try {await CheckOnceAsync(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
            catch(Exception exception) {logger.LogWarning(exception,"Cost-type automatic check did not complete; inspect the cost-type run history.");}
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope=scopes.CreateAsyncScope();
        var current=scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var store=scope.ServiceProvider.GetRequiredService<ITenantStore>();
        TenantConfiguration? nelson;
        using(current.Change(null)) nelson=await store.FindAsync("nelson");
        if(nelson is null)return;
        // The Elements integration is Nelson-only. Never apply another tenant's rules to its students.
        using(current.Change(nelson.Id))
            await scope.ServiceProvider.GetRequiredService<CostTypeAutomationRunner>().RunIfDueAsync(cancellationToken);
    }
}
