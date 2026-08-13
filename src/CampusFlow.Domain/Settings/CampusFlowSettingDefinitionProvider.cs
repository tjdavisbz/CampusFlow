using Volo.Abp.Settings;

namespace CampusFlow.Settings;

public class CampusFlowSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(CampusFlowSettings.MySetting1));
    }
}
