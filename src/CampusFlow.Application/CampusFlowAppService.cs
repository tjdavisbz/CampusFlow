using CampusFlow.Localization;
using Volo.Abp.Application.Services;

namespace CampusFlow;

/* Inherit your application services from this class.
 */
public abstract class CampusFlowAppService : ApplicationService
{
    protected CampusFlowAppService()
    {
        LocalizationResource = typeof(CampusFlowResource);
    }
}
