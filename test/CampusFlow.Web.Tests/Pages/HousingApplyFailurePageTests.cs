using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Web.Pages.Admin.Housing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp;
using Xunit;
namespace CampusFlow.Pages;
public class HousingApplyFailurePageTests
{
    [Fact]public async Task Known_preflight_failure_is_actionable_and_never_refreshes_or_saves_the_draft()
    {
        var service=Substitute.For<IHousingWorkspaceAppService>();service.ApplyAssignmentsAsync(Arg.Any<ApplyHousingDraftInput>()).Returns(Task.FromException<HousingWorkspaceState>(new UserFriendlyException("A student has a new Elements assignment. Review the saved draft.")));
        var page=new AssignmentsModel(service,new ConfigurationBuilder().Build());var result=await page.OnPostApplyAsync(new(){PeriodId=2,Confirmed=true});Assert.Equal(409,result.StatusCode);Assert.Contains("new Elements assignment",System.Text.Json.JsonSerializer.Serialize(result.Value));await service.DidNotReceiveWithAnyArgs().RefreshAsync(default,default);await service.DidNotReceiveWithAnyArgs().SaveAsync(default!);
    }
    [Fact]public async Task Unexpected_failures_are_not_exposed_to_the_browser()
    {
        var service=Substitute.For<IHousingWorkspaceAppService>();service.ApplyAssignmentsAsync(Arg.Any<ApplyHousingDraftInput>()).Returns(Task.FromException<HousingWorkspaceState>(new System.InvalidOperationException("private internal detail")));var page=new AssignmentsModel(service,new ConfigurationBuilder().Build());await Assert.ThrowsAsync<System.InvalidOperationException>(()=>page.OnPostApplyAsync(new(){PeriodId=2,Confirmed=true}));
    }
}
public class MealPlanSearchMetadataTests:CampusFlowWebTestBase
{
    [Fact]public void Search_query_does_not_become_a_required_assignment_field()
    {
        var metadata=GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>();Assert.False(metadata.GetMetadataForProperty(typeof(CampusFlow.Web.Pages.Admin.BusinessServices.AddStudentMealPlanModel),"Query").IsRequired);Assert.True(metadata.GetMetadataForProperty(typeof(CampusFlow.Web.Pages.Admin.BusinessServices.AddStudentMealPlanModel.AssignInputModel),"ExternalStudentId").IsRequired);
    }
}
