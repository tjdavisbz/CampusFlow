using Volo.Abp.Modularity;

namespace CampusFlow;

public abstract class CampusFlowApplicationTestBase<TStartupModule> : CampusFlowTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
