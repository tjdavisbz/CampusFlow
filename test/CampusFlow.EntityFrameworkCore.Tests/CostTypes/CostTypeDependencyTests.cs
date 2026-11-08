using CampusFlow.CostTypes;
using Shouldly;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.CostTypes;
public class CostTypeDependencyTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public void Cost_type_services_resolve_through_the_application_container()
    {
        GetRequiredService<ICostTypeElements>().ShouldNotBeNull();
        GetRequiredService<ICostTypeAppService>().ShouldNotBeNull();
        GetRequiredService<CostTypeRunExecutor>().ShouldNotBeNull();
        GetRequiredService<CostTypeAutomationRunner>().ShouldNotBeNull();
    }
}
