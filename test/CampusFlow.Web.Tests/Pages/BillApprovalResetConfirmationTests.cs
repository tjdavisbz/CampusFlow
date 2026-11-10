using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Web.Pages.Admin.StudentBilling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using Volo.Abp.AspNetCore.Mvc.UI.Alerts;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.Pages;

public class BillApprovalResetConfirmationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Only_a_confirmed_well_formed_post_can_reset(bool confirmed, bool valid)
    {
        var id=Guid.NewGuid();var service=Substitute.For<IResetBillApprovalAppService>();
        service.GetAsync(id).Returns(new ResetBillApprovalDto {Id=id,StudentName="Test Student",StudentId="TEST",TermCode="B27C",TermName="Spring 2027"});
        service.SearchAsync(Arg.Any<string?>(),Arg.Any<string?>()).Returns(new ResetBillApprovalSearchResultDto());
        var tenant=Substitute.For<ICurrentTenant>();tenant.Name.Returns("nelson");
        var alerts=Substitute.For<IAlertManager>();alerts.Alerts.Returns(new AlertList());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);lazy.LazyGetService<IAlertManager>().Returns(alerts);lazy.LazyGetRequiredService<IAlertManager>().Returns(alerts);
        var page=new ResetIndividualBillApprovalModel(service,Substitute.For<ITenantThemeProvider>())
        {LazyServiceProvider=lazy,PageContext=new PageContext {HttpContext=new DefaultHttpContext()},ResetInput=new(){ApprovalId=id,Confirmed=confirmed}};
        if(!valid)page.ModelState.AddModelError("ResetInput.Confirmed","Invalid submitted value");
        var result=await page.OnPostResetAsync();
        if(confirmed&&valid){Assert.IsType<RedirectToPageResult>(result);await service.Received(1).ResetAsync(id);}
        else{Assert.IsType<PageResult>(result);await service.DidNotReceiveWithAnyArgs().ResetAsync(default);}
    }
}

public class BillApprovalResetRenderedConfirmationTests : CampusFlowWebTestBase
{
    [Fact]
    public void Checkbox_uses_required_confirmation_without_a_numeric_range_validator()
    {
        var provider=GetRequiredService<IModelMetadataProvider>();var model=new ResetIndividualBillApprovalModel.ResetInputModel();
        var data=new ViewDataDictionary<ResetIndividualBillApprovalModel.ResetInputModel>(provider,new ModelStateDictionary()){Model=model};data.TemplateInfo.HtmlFieldPrefix="ResetInput";
        var context=new ViewContext {ViewData=data,HttpContext=new DefaultHttpContext {RequestServices=ServiceProvider},ClientValidationEnabled=true,FormContext=new FormContext()};
        var explorer=provider.GetModelExplorerForType(model.GetType(),model).GetExplorerForProperty("Confirmed");
        var tag=GetRequiredService<IHtmlGenerator>().GenerateCheckBox(context,explorer,"Confirmed",false,new {required="required"});
        using var writer=new StringWriter();tag.WriteTo(writer,HtmlEncoder.Default);var markup=writer.ToString();
        if(Environment.GetEnvironmentVariable("CF022_HTML_DIR") is {Length:>0} output){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"confirmation-checkbox.html"),markup);}
        Assert.Contains("required=",markup);Assert.Contains("name=\"ResetInput.Confirmed\"",markup);Assert.DoesNotContain("data-val-range",markup);
    }
}
