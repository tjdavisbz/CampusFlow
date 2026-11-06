using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Chapel;
public class ChapelDormEnvironmentTests
{
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Opposite_Elements_environment_is_rejected_before_connecting(bool nonprod)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["App:EnvironmentLabel"]=nonprod?"NON-PRODUCTION":"PRODUCTION",
            ["ConnectionStrings:ThesisElementsReadOnly"]=nonprod
                ?"Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;User ID=test;Password=unused"
                :"Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;User ID=test;Password=unused"
        }).Build();
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new ElementsChapelDormLookup(config).GetResidencesAsync(268));
        Assert.Contains("matching Elements environment",error.Message);
    }
}
