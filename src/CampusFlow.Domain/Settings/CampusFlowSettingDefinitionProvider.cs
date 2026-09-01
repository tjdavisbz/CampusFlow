using Volo.Abp.Settings;

namespace CampusFlow.Settings;

public class CampusFlowSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var localLogin = context.GetOrNull("Abp.Account.EnableLocalLogin");
        if (localLogin is not null)
        {
            localLogin.DefaultValue = false.ToString();
        }
    }
}
