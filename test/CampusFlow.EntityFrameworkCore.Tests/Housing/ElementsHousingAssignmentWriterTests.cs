using System;
using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public sealed class ElementsHousingAssignmentWriterTests
{
    [Fact]
    public void Verified_nonproduction_configuration_allows_writes()
    {
        Writer().EnsureWriteEnabled();
    }

    [Theory]
    [InlineData("Housing:EnableNonProductionWrites", "false")]
    [InlineData("App:EnvironmentLabel", "")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl", "https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl", "https://usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost", "el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;")]
    public void Write_guard_refuses_any_production_or_disabled_setting(string key, string value)
    {
        Should.Throw<InvalidOperationException>(() => Writer(key, value).EnsureWriteEnabled());
    }

    [Fact]
    public void Verified_production_configuration_allows_writes()
    {
        ProductionWriter().EnsureWriteEnabled();
    }

    [Theory]
    [InlineData("Housing:EnableProductionWrites", "false")]
    [InlineData("Housing:EnableNonProductionWrites", "true")]
    [InlineData("App:EnvironmentLabel", "NON-PRODUCTION")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl", "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl", "https://np-usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost", "el-np-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;")]
    public void Production_guard_refuses_disabled_or_cross_environment_settings(string key, string value)
    {
        Should.Throw<InvalidOperationException>(() => ProductionWriter(key, value).EnsureWriteEnabled());
    }

    private static ElementsHousingAssignmentWriter Writer(string? replaceKey = null, string? replaceValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Housing:EnableNonProductionWrites"] = "true",
            ["Housing:EnableProductionWrites"] = "false",
            ["App:EnvironmentLabel"] = "NON-PRODUCTION",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] =
                "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] =
                "https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "el-np-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"] =
                "Server=tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433;Database=EL-NP-NEL-DB;"
        };
        if (replaceKey is not null) values[replaceKey] = replaceValue;
        return new ElementsHousingAssignmentWriter(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static ElementsHousingAssignmentWriter ProductionWriter(string? replaceKey = null, string? replaceValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Housing:EnableProductionWrites"] = "true",
            ["Housing:EnableNonProductionWrites"] = "false",
            ["App:EnvironmentLabel"] = "",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] =
                "https://app-elm-prod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] =
                "https://el-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "el-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"] =
                "Server=tcp:sql-elm-prod-usc-replica.database.windows.net,1433;Database=EL-NEL-DB;"
        };
        if (replaceKey is not null) values[replaceKey] = replaceValue;
        return new ElementsHousingAssignmentWriter(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new MemoryCache(new MemoryCacheOptions()));
    }
}
