using System.Threading.Tasks;
using CampusFlow.Web.Menus;
using NSubstitute;
using Volo.Abp.AspNetCore.Mvc.UI.Layout;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Themes.LeptonXLite.Components.Menu;
using Volo.Abp.ObjectMapping;
using Volo.Abp.UI.Navigation;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalNavigationTests
{
    [Theory]
    [InlineData(CampusFlowMenus.Billing, true)]
    [InlineData(CampusFlowMenus.BillApproval, false)]
    public async Task Existing_billing_context_expands_the_Student_parent_in_the_installed_theme(string context, bool active)
    {
        var menu=new ApplicationMenu(StandardMenus.Main,"Main");
        var billing=new MenuItemViewModel {MenuItem=new ApplicationMenuItem(CampusFlowMenus.Billing,"Billing","~/Billing"),Items=[]};
        var student=new MenuItemViewModel {MenuItem=new ApplicationMenuItem(CampusFlowMenus.Student,"Student"),Items=[billing]};
        var model=new MenuViewModel {Menu=menu,Items=[student]};
        var manager=Substitute.For<IMenuManager>();manager.GetMainMenuAsync().Returns(menu);
        var layout=Substitute.For<IPageLayout>();layout.Content.Returns(new ContentLayout {MenuItemName=context});
        var mapper=Substitute.For<IObjectMapper<AbpAspNetCoreMvcUiLeptonXLiteThemeModule>>();
        mapper.Map<ApplicationMenu,MenuViewModel>(menu).Returns(model);
        var result=await new MenuViewModelProvider(manager,layout,mapper).GetMenuViewModelAsync();
        Assert.Equal(active,result.Items[0].IsActive);
        Assert.Equal(active,result.Items[0].Items[0].IsActive);
    }
}
