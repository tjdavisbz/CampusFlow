using System.Net;
using System.Threading.Tasks;
using CampusFlow.FinancialAid;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace CampusFlow.Pages;
public class FinancialAidAdminPreviewTests:CampusFlowWebTestBase
{
    [Fact]public async Task Sessions_without_the_award_permission_cannot_open_administrative_preview()
    {
        var response=await Client.GetAsync("/Admin/FinancialAid/ReconcileAwardCodes");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
    [Fact]public async Task Restored_service_resolves_with_registered_permission_and_read_only_adapter()
    {
        Assert.NotNull(GetRequiredService<IReconcileAwardCodesAppService>());
        Assert.False(GetRequiredService<IStudentInformationSystemAwardCodeMaintenance>().CanWrite);
        Assert.NotNull(await GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(CampusFlow.Permissions.CampusFlowPermissions.Admin.ReconcileAwardCodes));
    }
}
