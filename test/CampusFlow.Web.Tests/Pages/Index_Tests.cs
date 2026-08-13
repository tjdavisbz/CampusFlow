using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CampusFlow.Pages;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class Index_Tests : CampusFlowWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
