using CampusFlow.Identity;
using Castle.DynamicProxy;
using Shouldly;
using Volo.Abp.Identity;
using Xunit;

namespace CampusFlow.Identity.Tests;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class StaffUserDirectoryRegistrationTests : CampusFlowWebTestBase
{
    [Fact]
    public void Built_In_Users_Screen_Uses_The_Staff_Filtered_Service()
    {
        var service = GetRequiredService<IIdentityUserAppService>();
        var target = ((IProxyTargetAccessor)service).DynProxyGetTarget();
        target.ShouldBeOfType<CampusFlowIdentityUserAppService>();
    }
}
