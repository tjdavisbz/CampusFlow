using CampusFlow.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace CampusFlow.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class CampusFlowController : AbpControllerBase
{
    protected CampusFlowController()
    {
        LocalizationResource = typeof(CampusFlowResource);
    }
}
