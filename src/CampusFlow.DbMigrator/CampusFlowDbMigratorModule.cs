using CampusFlow.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace CampusFlow.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(CampusFlowEntityFrameworkCoreModule),
    typeof(CampusFlowApplicationContractsModule)
)]
public class CampusFlowDbMigratorModule : AbpModule
{
}
