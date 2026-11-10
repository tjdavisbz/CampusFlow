using Microsoft.Extensions.Configuration;
using CampusFlow.StudentInformationSystems;
using Shouldly;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public class ThesisElementsAwardCodeMaintenanceTests
{
    [Theory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    [InlineData(" yes ", true)]
    [InlineData("", false)]
    public void ElementsYesNoFieldsAreReadCorrectly(string value, bool expected)
    {
        ThesisElementsAwardCodeMaintenance.IsYes(value).ShouldBe(expected);
    }

    [Fact]
    public async System.Threading.Tasks.Task Legacy_enable_flags_cannot_enable_unverified_award_updates()
    {
        var settings=new System.Collections.Generic.Dictionary<string,string?>{["AwardCodeMaintenance:Enabled"]="true",["AwardCodeMaintenance:ExpectedApiBaseUrl"]="https://app-elm-prod-usc-integrationapi.azurewebsites.net",["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-prod-usc-integrationapi.azurewebsites.net"};
        var service=new ThesisElementsAwardCodeMaintenance(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build(),new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        service.CanWrite.ShouldBeFalse();
        await Should.ThrowAsync<System.InvalidOperationException>(()=>service.UpdateAsync(new(1,1,"100","Test Student","Spring 2027","Test award",1,2,false,true)));
        await Should.ThrowAsync<System.InvalidOperationException>(()=>service.GetTermsAsync());
    }
}
