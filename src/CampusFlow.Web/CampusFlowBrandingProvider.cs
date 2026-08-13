using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Localization;
using CampusFlow.Localization;

namespace CampusFlow.Web;

[Dependency(ReplaceServices = true)]
public class CampusFlowBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<CampusFlowResource> _localizer;

    public CampusFlowBrandingProvider(IStringLocalizer<CampusFlowResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
