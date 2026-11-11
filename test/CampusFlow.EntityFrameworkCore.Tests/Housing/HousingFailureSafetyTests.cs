using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public class HousingFailureSafetyTests
{
    [Theory][InlineData("{\"isSuccess\":true,\"statusCode\":200}")]
    [InlineData("{\"isSuccess\":true,\"data\":{\"success\":true}}")]
    public async Task Minimal_documented_and_extended_success_envelopes_remain_supported(string body)
    {
        var writer = Writer(200, body);
        await writer.AssignAsync(1, 2, 3);
    }
    [Theory]
    [InlineData(409, "{\"isSuccess\":true,\"data\":\"No rent data available for this room. private-student-value\"}", "rent-setup")]
    [InlineData(409, "{\"isSuccess\":true,\"data\":\"private-student-value\"}", "conflict")]
    [InlineData(400, "{\"isSuccess\":false,\"error\":\"private-student-value\"}", "rejected")]
    [InlineData(503, "private-student-value", "unavailable")]
    [InlineData(200, "{\"isSuccess\":false,\"error\":\"private-student-value\"}", "rejected")]
    [InlineData(200, "{\"isSuccess\":\"invalid\",\"data\":\"private-student-value\"}", "rejected")]
    [InlineData(200, "{\"isSuccess\":true,\"data\":{\"success\":false,\"message\":\"private-student-value\"}}", "rejected")]
    [InlineData(200, "{\"isSuccess\":true,\"data\":{\"success\":false,\"message\":\"already assigned private-student-value\"}}", "conflict")]
    public async Task Vendor_errors_are_classified_without_exposing_the_response(int status, string body, string code)
    {
        var writer = Writer(status, body);
        var error = await Assert.ThrowsAsync<HousingRequestException>(() => writer.AssignAsync(1, 2, 3));
        Assert.Equal(code, error.FailureCode); Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("private-student-value", error.ToString());
    }

    private static ElementsHousingAssignmentWriter Writer(int status, string body)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:EnvironmentLabel"] = "NON-PRODUCTION", ["Housing:EnableNonProductionWrites"] = "true",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] = "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] = "https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "el-np-nel.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:Username"] = "test",
            ["StudentInformationSystems:Providers:ThesisElements:Api:Password"] = "test",
            ["ConnectionStrings:ThesisElementsReadOnly"] = "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;"
        }).Build();
        return new ElementsHousingAssignmentWriter(config, new MemoryCache(new MemoryCacheOptions()), new HttpClient(new Handler(status, body)));
    }

    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath.Contains("Authenticate") ? HttpStatusCode.OK : (HttpStatusCode)status)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath.Contains("Authenticate")
                    ? "{\"isSuccess\":true,\"data\":{\"token\":\"test\"}}" : body, Encoding.UTF8, "application/json")
            });
    }
}
