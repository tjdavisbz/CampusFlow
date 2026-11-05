using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

public class BillingRecalculationServiceTests
{
    private sealed class Handler(HttpStatusCode status, bool success, bool offerRecalculate) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        public JsonNode? Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if(request.RequestUri.AbsolutePath.EndsWith("Authenticate"))return Response(HttpStatusCode.OK,"{\"data\":{\"token\":\"fake-token\"}}");
            Assert.Equal("Billing",string.Join("",request.Headers.GetValues("Module")));
            Assert.Equal("app-elm-nonprod-usc-integrationapi.azurewebsites.net",request.RequestUri.Host);
            if(request.Method==HttpMethod.Get)return Response(HttpStatusCode.OK,offerRecalculate ? "{\"isSuccess\":true,\"data\":{\"authorizeActions\":[{\"uniqueId\":\"Recalculate\"}]}}" : "{\"isSuccess\":true,\"data\":{\"authorizeActions\":[]}}");
            Payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Response(status,success ? "{\"isSuccess\":true}" : "{\"isSuccess\":false}");
        }
        private static HttpResponseMessage Response(HttpStatusCode status,string content)=>new(status){Content=new StringContent(content,Encoding.UTF8,"application/json")};
    }
    private static ThesisElementsBillingRecalculationService Service(Handler handler,bool enabled=true,bool productionTarget=false)
    {
        var p="StudentInformationSystems:Providers:ThesisElements:Api:";
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["App:EnvironmentLabel"]="NON-PRODUCTION",["BillApproval:EnableBillingRecalculation"]=enabled.ToString(),
            [p+"BaseUrl"]=productionTarget ? "https://app-elm-prod-usc-integrationapi.azurewebsites.net" : "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            [p+"TenantHost"]="el-np-nel.thesiscloud.com",[p+"Username"]="fake",[p+"Password"]="fake"
        }).Build();
        return new(configuration,new MemoryCache(new MemoryCacheOptions()),new HttpClient(handler));
    }

    [Fact]
    public async Task Uses_only_the_verified_Recalculate_action_and_exact_student_term_operation()
    {
        var handler=new Handler(HttpStatusCode.OK,true,true);var id=Guid.NewGuid();
        await Service(handler).RecalculateAsync("10","269",id);
        Assert.Equal("/api/billing/authorize/authorize-student",handler.Paths[2]);
        Assert.Equal("Recalculate",handler.Payload!["action"]!.GetValue<string>());
        Assert.Equal(10,handler.Payload["studentUId"]!.GetValue<int>());Assert.Equal(269,handler.Payload["termId"]!.GetValue<int>());
        Assert.Equal(id,handler.Payload["operationId"]!.GetValue<Guid>());
    }

    [Theory]
    [InlineData(202,true)]
    [InlineData(200,false)]
    public async Task Pending_or_unsuccessful_results_do_not_count_as_completed(int status,bool success)
    {
        var handler=new Handler((HttpStatusCode)status,success,true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler).RecalculateAsync("10","269",Guid.NewGuid()));
    }

    [Fact]
    public async Task Missing_Recalculate_action_prevents_any_billing_write()
    {
        var handler=new Handler(HttpStatusCode.OK,true,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler).RecalculateAsync("10","269",Guid.NewGuid()));
        Assert.Equal(2,handler.Paths.Count);Assert.Null(handler.Payload);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    public async Task Disabled_or_cross_environment_configuration_prevents_all_requests(bool enabled,bool production)
    {
        var handler=new Handler(HttpStatusCode.OK,true,true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler,enabled,production).RecalculateAsync("10","269",Guid.NewGuid()));
        Assert.Empty(handler.Paths);
    }
}
