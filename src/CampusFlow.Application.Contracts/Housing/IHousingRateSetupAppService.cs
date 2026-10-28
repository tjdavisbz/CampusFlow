using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.Housing;

public interface IHousingRateSetupAppService : IApplicationService
{
    Task<HousingRateScheduleDto> GetAsync(int periodId);
    Task<HousingRateScheduleDto> SaveAsync(HousingRateScheduleDto input);
}
