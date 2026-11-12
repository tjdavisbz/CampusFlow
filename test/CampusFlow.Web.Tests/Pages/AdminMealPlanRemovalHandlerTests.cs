using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using CampusFlow.Web.Pages.Admin.BusinessServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Xunit;
namespace CampusFlow.Pages;
public class AdminMealPlanRemovalHandlerTests
{
    [Theory][InlineData(false,true)][InlineData(true,false)]
    public async Task Local_None_clear_requires_removal_permission_and_confirmation(bool granted,bool confirmed)
    {
        var service=Substitute.For<IAdminMealPlanAppService>();var auth=Substitute.For<IAuthorizationService>();
        auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.RemoveStudentMealPlan).Returns(granted?AuthorizationResult.Success():AuthorizationResult.Failed());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<IAuthorizationService>().Returns(auth);
        var page=new AddStudentMealPlanModel(service,Substitute.For<ITenantThemeProvider>(),NullLogger<AddStudentMealPlanModel>.Instance)
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()},ClearInput=new(){ExternalStudentId="123",TermCalendarId=269,SelectionId=System.Guid.NewGuid(),ConcurrencyStamp="current",Reason="Student request",Confirmed=confirmed}};
        var result=await page.OnPostClearNoMealPlanAsync();if(granted)Assert.IsType<BadRequestResult>(result);else Assert.IsType<ForbidResult>(result);
        await service.DidNotReceiveWithAnyArgs().ClearNoMealPlanSelectionAsync(default!);
    }
    [Theory][InlineData(false,true)][InlineData(true,false)]
    public async Task Permission_and_confirmation_are_checked_before_calling_the_service(bool granted,bool confirmed)
    {
        var service=Substitute.For<IAdminMealPlanAppService>();var auth=Substitute.For<IAuthorizationService>();
        auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.RemoveStudentMealPlan).Returns(granted?AuthorizationResult.Success():AuthorizationResult.Failed());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<IAuthorizationService>().Returns(auth);
        var page=new AddStudentMealPlanModel(service,Substitute.For<ITenantThemeProvider>(),NullLogger<AddStudentMealPlanModel>.Instance)
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()},RemoveInput=new(){ExternalStudentId="123",TermCalendarId=269,AssignmentId=10,MealPlanId=20,Confirmed=confirmed}};
        var result=await page.OnPostRemoveAsync();if(granted)Assert.IsType<BadRequestResult>(result);else Assert.IsType<ForbidResult>(result);
        await service.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
    }
    [Fact]
    public async Task Removal_preview_requires_removal_permission_before_reading_financial_entries()
    {
        var service=Substitute.For<IAdminMealPlanAppService>();var auth=Substitute.For<IAuthorizationService>();
        auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.RemoveStudentMealPlan).Returns(AuthorizationResult.Failed());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<IAuthorizationService>().Returns(auth);
        var page=new AddStudentMealPlanModel(service,Substitute.For<ITenantThemeProvider>(),NullLogger<AddStudentMealPlanModel>.Instance)
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()}};
        Assert.IsType<ForbidResult>(await page.OnPostPreviewRemovalAsync());
        await service.DidNotReceiveWithAnyArgs().PreviewRemovalAsync(default!);
    }
    [Theory][InlineData("missing-preview")][InlineData("short-reason")][InlineData("unchecked")]
    public async Task A_direct_old_or_unreviewed_removal_form_cannot_start_billing_cleanup(string invalid)
    {
        var service=Substitute.For<IAdminMealPlanAppService>();var auth=Substitute.For<IAuthorizationService>();
        auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.RemoveStudentMealPlan).Returns(AuthorizationResult.Success());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<IAuthorizationService>().Returns(auth);
        var input=new RemoveStudentMealPlanInput{ExternalStudentId="123",TermCalendarId=269,AssignmentId=10,MealPlanId=20,Confirmed=true,ReviewRevision="current",Reason="Student requested removal"};
        if(invalid=="missing-preview")input.ReviewRevision="";else if(invalid=="short-reason")input.Reason="short";else input.Confirmed=false;
        var page=new AddStudentMealPlanModel(service,Substitute.For<ITenantThemeProvider>(),NullLogger<AddStudentMealPlanModel>.Instance)
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()},RemoveInput=input};
        Assert.IsType<BadRequestResult>(await page.OnPostRemoveAsync());await service.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
    }

}
