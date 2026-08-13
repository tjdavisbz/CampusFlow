using Volo.Abp.Modularity;

namespace CampusFlow;

[DependsOn(
    typeof(CampusFlowDomainModule),
    typeof(CampusFlowTestBaseModule)
)]
public class CampusFlowDomainTestModule : AbpModule
{

}
