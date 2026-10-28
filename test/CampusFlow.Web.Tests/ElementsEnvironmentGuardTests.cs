using System;
using System.Collections.Generic;
using CampusFlow.Web;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CampusFlow.Web.Tests;

public sealed class ElementsEnvironmentGuardTests
{
    [Fact]
    public void Nonproduction_settings_are_accepted()
    {
        ElementsEnvironmentGuard.Validate(Configuration());
    }

    [Theory]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl", "https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl", "https://usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost", "el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;")]
    public void Nonproduction_refuses_production_elements_setting(string key, string value)
    {
        Should.Throw<InvalidOperationException>(() =>
            ElementsEnvironmentGuard.Validate(Configuration(new KeyValuePair<string, string?>(key, value))));
    }

    private static IConfiguration Configuration(KeyValuePair<string, string?>? replacement = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["App:EnvironmentLabel"] = "NON-PRODUCTION",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] =
                "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] =
                "https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "el-np-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"] =
                "Server=tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433;Database=EL-NP-NEL-DB;"
        };
        if (replacement.HasValue) values[replacement.Value.Key] = replacement.Value.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
