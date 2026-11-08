using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using CampusFlow.Web.Pages.Admin.StudentBilling;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
namespace CampusFlow.Pages;
public class CostTypePreviewScopeTests
{
    [Fact]
    public async Task Invalid_schedule_input_never_reaches_the_service()
    {
        var service=Substitute.For<ICostTypeAppService>();var page=new CostTypesModel(service);
        page.ModelState.AddModelError("Enabled","Invalid value");
        Assert.IsType<BadRequestResult>(await page.OnPostAutomationAsync(new()));
        await service.DidNotReceiveWithAnyArgs().SetAutomaticRunsAsync(default!);
    }
    [Theory] [InlineData(null)] [InlineData(0)] [InlineData(-1)]
    public async Task Invalid_UID_cannot_fall_back_to_previewing_all_students(int? uid)
    {
        var service=Substitute.For<ICostTypeAppService>();var page=new CostTypesModel(service);
        if(uid is null)page.ModelState.AddModelError("studentUid","Value exceeds the Elements UID range.");
        Assert.Equal(400,(await page.OnPostPreviewAsync(uid)).StatusCode);
        await service.DidNotReceiveWithAnyArgs().PreviewAsync(default);
    }
    [Theory] [InlineData(null)] [InlineData(8313)]
    public async Task Valid_blank_or_specific_UID_retains_its_exact_scope(int? uid)
    {
        var service=Substitute.For<ICostTypeAppService>();
        service.PreviewAsync(uid).Returns(new CostTypePreviewDto(null,[],"Preview only"));
        var page=new CostTypesModel(service);
        var response=await page.OnPostPreviewAsync(uid);
        Assert.Null(response.StatusCode);Assert.IsType<CostTypePreviewDto>(response.Value);
        await service.Received(1).PreviewAsync(uid);
    }
}
