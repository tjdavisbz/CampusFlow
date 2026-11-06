using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Students;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillingServiceRegistrationTests : CampusFlowWebTestBase
{
    [Fact]
    public void New_billing_services_resolve_once_through_the_application_container()
    {
        Assert.NotNull(GetRequiredService<IStudentTermSelection>());
        Assert.Single(GetRequiredService<IEnumerable<IStudentInformationSystemBillingRecalculationService>>(),
            x=>x.Provider==StudentInformationSystemProvider.ThesisElements);
    }
    [Fact]
    public void Completion_services_and_the_background_job_are_registered()
    {
        GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "wwwroot");
        Assert.NotNull(GetRequiredService<CampusFlow.Web.BillApprovals.BillApprovalCompletionJob>());
        Assert.NotNull(GetRequiredService<CampusFlow.Web.BillApprovals.IBillApprovalCompletionOperations>());
        Assert.IsType<CampusFlow.Web.BillApprovals.AzureBillApprovalMailSender>(GetRequiredService<CampusFlow.Web.BillApprovals.IBillApprovalMailSender>());
        Assert.Single(GetRequiredService<IEnumerable<IStudentInformationSystemBillClearanceService>>(),x=>x.Provider==StudentInformationSystemProvider.ThesisElements);
    }

}
