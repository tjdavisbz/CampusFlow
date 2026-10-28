using System;
using System.Collections.Generic;
using CampusFlow.Web.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CampusFlow.Web.Tests.StudentInformationSystems;

public class NonProductionElementsGuardTests
{
    [Fact]
    public void ApprovedNonProductionEndpointsStart()
    {
        Should.NotThrow(() => NonProductionElementsGuard.Validate("Staging", Settings()));
    }

    [Theory]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl",
        "https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl",
        "https://el-usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost",
        "el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly",
        "Server=tcp:sql-elm-prod-usc-replica.database.windows.net,1433;Database=EL-NEL-DB")]
    public void ProductionFallbackBlocksStagingStartup(string key, string value)
    {
        Should.Throw<InvalidOperationException>(() =>
            NonProductionElementsGuard.Validate("Staging", Settings(key, value)));
    }

    [Fact]
    public void ProductionEnvironmentIsNotAffected()
    {
        Should.NotThrow(() => NonProductionElementsGuard.Validate("Production", Settings(
            "StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl",
            "https://app-elm-prod-usc-integrationapi.azurewebsites.net")));
    }

    private static IConfiguration Settings(string? overrideKey = null, string? overrideValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] =
                "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] =
                "https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] =
                "el-np-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"] =
                "Server=tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433;Database=EL-NP-NEL-DB"
        };
        if (overrideKey is not null) values[overrideKey] = overrideValue;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
