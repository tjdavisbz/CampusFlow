using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.Web.ClearanceResets;
public sealed class ClearanceResetWorker(IServiceScopeFactory scopes,ILogger<ClearanceResetWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var tenant=scope.ServiceProvider.GetRequiredService<ICurrentTenant>();var store=scope.ServiceProvider.GetRequiredService<ITenantStore>();
                TenantConfiguration? nelson;using(tenant.Change(null))nelson=await store.FindAsync("nelson");
                if(nelson is not null)using(tenant.Change(nelson.Id))await scope.ServiceProvider.GetRequiredService<ClearanceResetRunner>().RunIfDueAsync(stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Clearance reset check did not complete; inspect its run history.");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
