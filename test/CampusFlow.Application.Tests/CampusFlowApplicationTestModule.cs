using Volo.Abp.Modularity;

namespace CampusFlow;

[DependsOn(
    typeof(CampusFlowApplicationModule),
    typeof(CampusFlowDomainTestModule)
)]
public class CampusFlowApplicationTestModule : AbpModule
{

}
