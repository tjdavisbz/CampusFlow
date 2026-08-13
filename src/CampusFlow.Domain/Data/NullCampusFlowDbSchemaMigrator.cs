using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Data;

/* This is used if database provider does't define
 * ICampusFlowDbSchemaMigrator implementation.
 */
public class NullCampusFlowDbSchemaMigrator : ICampusFlowDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
