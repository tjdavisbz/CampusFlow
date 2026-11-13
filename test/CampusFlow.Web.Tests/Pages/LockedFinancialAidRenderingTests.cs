using System.IO;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusFlow.Pages;

public class LockedFinancialAidRenderingTests : CampusFlowWebTestBase
{
    [Fact]
    public async Task Locked_pending_awards_render_accept_decline_and_semester_accept_all()
    {
        var html=await Render(FinancialAidBatchTests.Award() with{IsLocked=true,IsLoan=true});
        Assert.Contains("data-aid-decision=\"Accept\"",html);
        Assert.Contains("data-aid-decision=\"Decline\"",html);
        Assert.Contains("data-aid-accept-all",html);
        Assert.DoesNotContain("Not available",html);
    }
    [Theory][InlineData("read-only")][InlineData("billed")][InlineData("cancelled")][InlineData("accepted")][InlineData("declined")][InlineData("zero")]
    public async Task Lock_policy_does_not_expose_actions_for_other_unavailable_awards(string condition)
    {
        var award=FinancialAidBatchTests.Award() with{IsLocked=true};
        award=condition switch{
            "billed"=>award with{IsSentToBilling=true},"cancelled"=>award with{AwardStatus="Cancelled"},
            "accepted"=>award with{StudentAccepted=1},"declined"=>award with{StudentAccepted=0},"zero"=>award with{Amount=0},_=>award};
        var html=await Render(award,condition=="read-only");
        Assert.DoesNotContain("data-aid-decision",html);Assert.DoesNotContain("data-aid-accept-all",html);
    }
    private async Task<string> Render(StudentFinancialAidAward award,bool readOnly=false)
    {
        using var scope=GetRequiredService<IServiceScopeFactory>().CreateScope();
        var services=scope.ServiceProvider;
        var context=new ActionContext(new DefaultHttpContext{RequestServices=services},new RouteData(),new ActionDescriptor());
        var view=services.GetRequiredService<IRazorViewEngine>().GetView(null,"/Pages/_FinancialAidTerm.cshtml",false);
        Assert.True(view.Success);
        var data=new ViewDataDictionary<FinancialAidModel.AwardTermGroup>(new EmptyModelMetadataProvider(),new ModelStateDictionary())
            {Model=new("B27C","Spring 2027",100m,new[]{award})};
        data["ReadOnly"]=readOnly;
        using var output=new StringWriter();
        await view.View.RenderAsync(new ViewContext(context,view.View,data,
            new TempDataDictionary(context.HttpContext,services.GetRequiredService<ITempDataProvider>()),output,new HtmlHelperOptions()));
        return output.ToString();
    }
}
