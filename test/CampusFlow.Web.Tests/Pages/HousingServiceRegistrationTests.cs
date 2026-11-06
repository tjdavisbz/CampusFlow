using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Xunit;
namespace CampusFlow.Pages;
public class HousingServiceRegistrationTests:CampusFlowWebTestBase
{
    [Fact]public void Housing_administration_and_meal_removal_resolve_through_the_real_container()
    {Assert.NotNull(GetRequiredService<IHousingWorkspaceAppService>());Assert.NotNull(GetRequiredService<IAdminMealPlanAppService>());Assert.IsType<ThesisElementsMealPlanRemovalService>(GetRequiredService<IStudentInformationSystemMealPlanRemovalService>());}
}
