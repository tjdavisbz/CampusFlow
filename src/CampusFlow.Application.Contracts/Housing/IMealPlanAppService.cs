using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.Housing;

public interface IMealPlanAppService : IApplicationService
{
    Task<MealPlanSelectionDto> GetAsync(string? externalTermId = null);
    Task<MealPlanSelectionDto> SaveAsync(SaveMealPlanSelectionInput input);
}
