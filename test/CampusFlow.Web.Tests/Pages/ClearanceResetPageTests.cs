using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using CampusFlow.Students;
using CampusFlow.Web.Pages.Admin.StudentBilling;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using Xunit;
namespace CampusFlow.Pages;
public class ClearanceResetPageTests
{
    [Theory][InlineData(StudentViewClaimTypes.ExternalStudentId)][InlineData(FacultyViewClaimTypes.FacultyId)][InlineData(CampusFlow.AdvisorPortal.SchedulerViewClaimTypes.Email)]
    public async Task Read_only_views_cannot_change_the_job(string claim)
    {var service=Substitute.For<IClearanceResetAppService>();var context=new DefaultHttpContext();context.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim,"test")],"test"));var page=new ClearanceResetsModel(service){PageContext=new PageContext{HttpContext=context}};Assert.True(page.IsReadOnlyView);Assert.IsType<ForbidResult>(await page.OnPostScheduleAsync(new(){Enabled=true}));Assert.IsType<ForbidResult>(await page.OnPostPreviewAsync(null));Assert.IsType<ForbidResult>(await page.OnPostRunAsync(new()));Assert.IsType<ForbidResult>(await page.OnPostExceptionPreviewAsync(10,269));Assert.IsType<ForbidResult>(await page.OnPostExceptionAsync(new()));await service.DidNotReceiveWithAnyArgs().PreviewAsync(default);await service.DidNotReceiveWithAnyArgs().RunAsync(default!);await service.DidNotReceiveWithAnyArgs().SetAsync(default!);}
    [Fact]public async Task Malformed_input_never_reaches_the_service()
    {var service=Substitute.For<IClearanceResetAppService>();var page=new ClearanceResetsModel(service);page.ModelState.AddModelError("Enabled","Invalid boolean");Assert.IsType<BadRequestResult>(await page.OnPostScheduleAsync(new()));await service.DidNotReceiveWithAnyArgs().SetAsync(default!);}
    [Theory][InlineData(0)][InlineData(-1)][InlineData(null)]public async Task Invalid_or_overflowing_UID_never_previews_everyone(int? uid)
    {var service=Substitute.For<IClearanceResetAppService>();var page=new ClearanceResetsModel(service);if(uid is null)page.ModelState.AddModelError("studentUid","Overflow");Assert.IsType<BadRequestResult>(await page.OnPostPreviewAsync(uid));await service.DidNotReceiveWithAnyArgs().PreviewAsync(default);}

}
