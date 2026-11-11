using System;using System.Security.Claims;using System.Threading.Tasks;using CampusFlow.Branding;using CampusFlow.Housing;using CampusFlow.Permissions;using CampusFlow.Web.Pages.Admin.BusinessServices;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Http;using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;using Microsoft.Extensions.Logging.Abstractions;using NSubstitute;using Volo.Abp.AspNetCore.Mvc.UI.Alerts;using Volo.Abp.DependencyInjection;using Volo.Abp.MultiTenancy;using Xunit;
namespace CampusFlow.Pages;
public class AdminMealPlanAssignmentHandlerTests
{
    static(AddStudentMealPlanModel page,IAdminMealPlanAppService service) Setup()
    {
        var service=Substitute.For<IAdminMealPlanAppService>();service.GetContextAsync("123",269).Returns(new AdminMealPlanContextDto{Student=new AdminMealPlanStudentDto{ExternalStudentId="123",StudentId="test",Name="Test"},SelectedTermCalendarId=269,HousingStatuses={new AdminHousingStatusDto{Id=10,Name="Commuter"}},Options={new AdminMealPlanOptionDto{MealPlanId=20,Name="Plan"}}});
        var auth=Substitute.For<IAuthorizationService>();auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.RemoveStudentMealPlan).Returns(AuthorizationResult.Success());var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<IAuthorizationService>().Returns(auth);var alerts=Substitute.For<IAlertManager>();alerts.Alerts.Returns(new AlertList());lazy.LazyGetRequiredService<IAlertManager>().Returns(alerts);var tenant=Substitute.For<ICurrentTenant>();tenant.Name.Returns("nelson");lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var page=new AddStudentMealPlanModel(service,Substitute.For<ITenantThemeProvider>(),NullLogger<AddStudentMealPlanModel>.Instance){LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()},Input=new(){ExternalStudentId="123",TermCalendarId=269,HousingStatusId=10,MealPlanId=20,Confirmed=true}};return(page,service);
    }
    [Fact] public async Task Errors_from_the_other_form_do_not_silently_block_a_valid_assignment()
    {var(p,s)=Setup();p.ModelState.AddModelError("RemoveInput.ExternalStudentId","The removal input is required.");p.ModelState.AddModelError("ClearInput.Reason","A reason is required for clearing a choice.");p.ModelState.AddModelError("Query","The Query field is required.");Assert.IsType<RedirectToPageResult>(await p.OnPostAssignAsync());await s.Received(1).AssignAsync(Arg.Is<AssignStudentMealPlanInput>(x=>x.ExternalStudentId=="123"&&x.TermCalendarId==269&&x.MealPlanId==20));Assert.True(p.CanRemove);}
    [Theory] [InlineData("Input.MealPlanId")] [InlineData("Input.TermCalendarId")]
    public async Task Active_form_validation_still_blocks_invalid_input(string key)
    {var(p,s)=Setup();p.ModelState.AddModelError(key,"Invalid assignment input");Assert.IsType<PageResult>(await p.OnPostAssignAsync());await s.DidNotReceiveWithAnyArgs().AssignAsync(default!);Assert.True(p.CanRemove);}
    [Fact] public async Task The_displayed_default_housing_status_is_also_used_in_the_confirmation()
    {var(p,s)=Setup();p.Student="123";p.Term=269;await p.OnGetAsync();Assert.Equal(10,p.HousingStatus);await s.DidNotReceiveWithAnyArgs().AssignAsync(default!);}
    [Fact] public async Task Invalid_submission_retains_the_reviewed_student_term_and_plan()
    {var(p,s)=Setup();p.Input.Confirmed=false;await p.OnPostAssignAsync();Assert.Equal("123",p.Student);Assert.Equal(269,p.Term);Assert.Equal(10,p.HousingStatus);Assert.Equal(20,p.MealPlan);}
    [Fact] public async Task Confirmation_is_still_required()
    {var(p,s)=Setup();p.Input.Confirmed=false;Assert.IsType<PageResult>(await p.OnPostAssignAsync());await s.DidNotReceiveWithAnyArgs().AssignAsync(default!);}
}
