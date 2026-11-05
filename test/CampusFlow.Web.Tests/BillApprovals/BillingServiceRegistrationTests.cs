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
}
