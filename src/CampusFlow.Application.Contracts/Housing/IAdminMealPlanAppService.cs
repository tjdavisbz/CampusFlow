using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.Housing;

public interface IAdminMealPlanAppService : IApplicationService
{
    Task<List<AdminMealPlanStudentDto>> SearchStudentsAsync(string query);
    Task<AdminMealPlanContextDto> GetContextAsync(string externalStudentId, int? termCalendarId = null);
    Task AssignAsync(AssignStudentMealPlanInput input);
}
