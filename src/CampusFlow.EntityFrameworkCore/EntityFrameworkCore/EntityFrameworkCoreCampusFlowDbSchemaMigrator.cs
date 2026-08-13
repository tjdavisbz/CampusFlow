using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CampusFlow.Data;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.EntityFrameworkCore;

public class EntityFrameworkCoreCampusFlowDbSchemaMigrator
    : ICampusFlowDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreCampusFlowDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the CampusFlowDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<CampusFlowDbContext>()
            .Database
            .MigrateAsync();
    }
}
