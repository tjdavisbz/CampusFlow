using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.Housing;

public interface IMealPlanAppService : IApplicationService
{
    Task<MealPlanSelectionDto> GetAsync();
    Task<MealPlanSelectionDto> SaveAsync(SaveMealPlanSelectionInput input);
}
