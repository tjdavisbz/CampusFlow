using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public class HousingHistoricalAssignmentWriterTests
{
    private static HousingHistoricalAssignment Row() => new(1,10,2,900,21,2100,"Commuter","Old",true,true,true,null,null);
    private sealed class Handler : HttpMessageHandler
    {
        public bool Active, FailDeallocation;
        public readonly List<JsonObject> Writes = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            JsonNode data;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/deallocate",path);
                Writes.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject());
                if (FailDeallocation) return Response(HttpStatusCode.Conflict,new JsonObject());
                data = new JsonObject { ["success"] = true };
            }
            else if (path.Contains("Authenticate")) data = new JsonObject { ["token"] = "test" };
            else if (path.Contains("get-assigned-rooms")) data = Active
                ? new JsonArray(new JsonObject { ["housingPeriodID"]=2,["housingPeriodPropertyID"]=21,["roomID"]=2100,["building"]="Commuter",["roomName"]="New" })
                : new JsonArray();
            else data = new JsonObject { ["housingPeriodPropertyID"]=21,["housingPeriodID"]=2,["propertyName"]="Commuter",
                ["rooms"]=new JsonArray(new JsonObject { ["housingPeriodRoomID"]=999,["housingPeriodID"]=2,["roomName"]="New" }) };
            return Response(HttpStatusCode.OK,data);
        }
        private static HttpResponseMessage Response(HttpStatusCode status,JsonNode data) => new(status)
        { Content=new StringContent(new JsonObject { ["isSuccess"]=status==HttpStatusCode.OK,["data"]=data }.ToJsonString(),Encoding.UTF8,"application/json") };
    }
    private static ElementsHousingAssignmentWriter Writer(Handler handler,Func<List<HousingHistoricalAssignment>> history)
    {
        var values=new Dictionary<string,string?> {
            ["Housing:EnableNonProductionWrites"]="true",["App:EnvironmentLabel"]="NON-PRODUCTION",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="test",
            ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;" };
        return new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),new MemoryCache(new MemoryCacheOptions()),
            new HttpClient(handler),historicalAssignments:(_,_,_)=>Task.FromResult(history()));
    }
    [Fact] public async Task Preview_is_read_only_and_deallocation_targets_only_the_exact_old_room()
    {
        var handler=new Handler();var writer=Writer(handler,()=>[Row()]);
        Assert.Single(await writer.GetHistoricalAssignmentsAsync(2,[10]));Assert.Empty(handler.Writes);
        await writer.ClearHistoricalAssignmentAsync(Row());
        var write=Assert.Single(handler.Writes);
        Assert.Equal(900,write["housingPeriodRoomId"]!.GetValue<int>());Assert.Equal(2,write["housingPeriodId"]!.GetValue<int>());
        Assert.Equal(10,Assert.Single(write["studentUIds"]!.AsArray())!.GetValue<int>());
    }
    [Fact] public async Task A_live_assignment_blocks_historical_deallocation()
    {
        var handler=new Handler { Active=true };var writer=Writer(handler,()=>[Row()]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.ClearHistoricalAssignmentAsync(Row()));Assert.Empty(handler.Writes);
    }
    [Fact] public async Task Changed_history_blocks_deallocation_before_dispatch()
    {
        var handler=new Handler();var writer=Writer(handler,()=>[Row() with { AssignmentId=2 }]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.ClearHistoricalAssignmentAsync(Row()));Assert.Empty(handler.Writes);
    }
    [Theory] [InlineData("student")] [InlineData("period")] [InlineData("duplicate")]
    public async Task Unscoped_or_ambiguous_history_is_rejected(string problem)
    {
        var rows=problem=="student"?new List<HousingHistoricalAssignment>{Row() with {StudentUid=11}}:
            problem=="period"?[Row() with {PeriodId=3}]:[Row(),Row() with {AssignmentId=2}];
        var handler=new Handler();var writer=Writer(handler,()=>rows);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.GetHistoricalAssignmentsAsync(2,[10]));Assert.Empty(handler.Writes);
    }
    [Fact] public async Task Failed_vendor_deallocation_is_not_retried_or_treated_as_success()
    {
        var handler=new Handler { FailDeallocation=true };var writer=Writer(handler,()=>[Row()]);
        await Assert.ThrowsAsync<HousingRequestException>(()=>writer.ClearHistoricalAssignmentAsync(Row()));Assert.Single(handler.Writes);
    }
}
