using CampusFlow.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace CampusFlow.Web.Pages;

public abstract class CampusFlowPageModel : AbpPageModel
{
    protected CampusFlowPageModel()
    {
        LocalizationResourceType = typeof(CampusFlowResource);
    }
}
