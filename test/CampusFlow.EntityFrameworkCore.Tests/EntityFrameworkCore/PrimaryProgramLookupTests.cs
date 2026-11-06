using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.EntityFrameworkCore;
public class PrimaryProgramLookupTests
{
    [Theory]
    [InlineData("NON-PRODUCTION", "sql-elm-prod-usc-replica.database.windows.net", "EL-NEL-DB")]
    [InlineData("PRODUCTION", "sql-elm-nonprod-usc-replica.database.windows.net", "EL-NP-NEL-DB")]
    [InlineData("NON-PRODUCTION", "sql-elm-nonprod-usc-replica.database.windows.net", "EL-NEL-DB")]
    public async Task Reporting_connection_must_match_the_environment(string environment, string server, string database)
    {
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(Guid.NewGuid()); tenant.Name.Returns("nelson");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["App:EnvironmentLabel"]=environment, ["ConnectionStrings:ThesisElementsReadOnly"]=$"Server={server};Database={database};Integrated Security=true" }).Build();
        var lookup = new ElementsPrimaryProgramLookup(config, tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.GetAsync([new("100", "269")]));
    }
    [Theory]
    [InlineData("other")]
    [InlineData(null)]
    public async Task Another_tenant_cannot_query_Nelson_program_data(string? name)
    {
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(Guid.NewGuid()); tenant.Name.Returns(name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ElementsPrimaryProgramLookup(new ConfigurationBuilder().Build(), tenant).GetChoicesAsync());
    }
    [Fact]
    public async Task Invalid_identifiers_are_rejected_before_any_SQL_connection()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        await Assert.ThrowsAsync<ArgumentException>(() => new ElementsPrimaryProgramLookup(new ConfigurationBuilder().Build(), tenant).GetAsync([new("100 OR 1=1", "269")]));
    }
}
