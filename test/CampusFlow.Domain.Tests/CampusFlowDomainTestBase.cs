using Volo.Abp.Modularity;

namespace CampusFlow;

/* Inherit from this class for your domain layer tests. */
public abstract class CampusFlowDomainTestBase<TStartupModule> : CampusFlowTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
