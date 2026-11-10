using CampusFlow.MealPlanTaxes;
using Xunit;
namespace CampusFlow.BillApprovals;
public class MealPlanTaxDependencyTests:CampusFlowWebTestBase
{
    [Fact]public void Tax_services_resolve_in_the_real_portal_container_without_contacting_Elements()
    {
        Assert.NotNull(GetRequiredService<IMealPlanTaxCoordinator>());
        Assert.NotNull(GetRequiredService<IElementsMealPlanTaxGateway>());
        Assert.NotNull(GetRequiredService<IElementsMealPlanTaxReadModel>());
        Assert.NotNull(GetRequiredService<IMealPlanTaxChargeStore>());
    }
}
