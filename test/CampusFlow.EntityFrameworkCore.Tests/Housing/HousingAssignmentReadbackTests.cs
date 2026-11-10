using System;
using System.Collections.Generic;
using System.Linq;
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
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class HousingAssignmentReadbackTests
{
    private static JsonObject Assigned(int property,string room)=>new(){["housingPeriodID"]=2,["housingPeriodPropertyID"]=property,["roomID"]=property*100,["building"]="Commuter",["roomName"]=room};
    private static JsonObject Property(int id,params string[] names)=>new(){["housingPeriodPropertyID"]=id,["housingPeriodID"]=2,["propertyName"]="Commuter",["rooms"]=new JsonArray(names.Select((name,i)=>(JsonNode)new JsonObject{["housingPeriodRoomID"]=id*100+i,["housingPeriodID"]=2,["roomName"]=name}).ToArray())};
    [Fact]public async Task Deleted_assignments_to_rooms_absent_from_live_property_do_not_block_a_saved_draft()
    {var(writer,http)=Setup(new JsonArray(Assigned(21,"Old commuter room")),new(){[21]=Property(21)});Assert.Null(await writer.GetAssignedRoomAsync(10,2));Assert.All(http.Methods,x=>Assert.Equal(HttpMethod.Get,x));}
    [Fact]public async Task Current_assignment_still_blocks_a_second_assignment_even_when_old_rows_are_returned()
    {var(writer,http)=Setup(new JsonArray(Assigned(21,"Old"),Assigned(22,"New")),new(){[21]=Property(21),[22]=Property(22,"New")});Assert.Equal(("Commuter","New"),await writer.GetAssignedRoomAsync(10,2));}
    [Fact]public async Task Two_current_assignments_are_never_treated_as_unassigned()
    {var(writer,_)=Setup(new JsonArray(Assigned(21,"One"),Assigned(22,"Two")),new(){[21]=Property(21,"One"),[22]=Property(22,"Two")});await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.GetAssignedRoomAsync(10,2));}
    [Fact]public async Task Missing_room_without_exact_deleted_assignment_proof_remains_a_live_conflict()
    {var(writer,_)=Setup(new JsonArray(Assigned(21,"One")),new(){[21]=Property(21)},deleted:false);Assert.Equal(("Commuter","One"),await writer.GetAssignedRoomAsync(10,2));}
    [Fact]public async Task Failed_property_read_never_turns_an_assignment_into_unassigned()
    {var(writer,http)=Setup(new JsonArray(Assigned(21,"One")),new());http.Fail=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.GetAssignedRoomAsync(10,2));}
    [Theory][InlineData("housingPeriodID")][InlineData("housingPeriodPropertyID")][InlineData("propertyName")][InlineData("rooms")]
    public async Task Mismatched_or_incomplete_property_metadata_fails_closed(string field)
    {var p=Property(21,"One");p.Remove(field);var(writer,_)=Setup(new JsonArray(Assigned(21,"One")),new(){[21]=p});await Assert.ThrowsAnyAsync<Exception>(()=>writer.GetAssignedRoomAsync(10,2));}
    private static (ElementsHousingAssignmentWriter,Handler) Setup(JsonArray rows,Dictionary<int,JsonObject> properties,bool deleted=true)
    {
        var values=new Dictionary<string,string?> { ["Housing:EnableNonProductionWrites"]="true",["Housing:EnableProductionWrites"]="false",["App:EnvironmentLabel"]="NON-PRODUCTION",["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="test",["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;"};
        var handler=new Handler(rows,properties);return(new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),new MemoryCache(new MemoryCacheOptions()),new HttpClient(handler),(_,_,_,_,_,_)=>Task.FromResult(deleted)),handler);
    }
    private sealed class Handler(JsonArray rows,Dictionary<int,JsonObject> properties):HttpMessageHandler
    {
        public bool Fail;public List<HttpMethod> Methods=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Methods.Add(request.Method);var path=request.RequestUri!.AbsolutePath;
            JsonNode? data=path.Contains("Authenticate")?new JsonObject{["token"]="test"}:path.Contains("get-assigned-rooms")?rows:properties.GetValueOrDefault(int.Parse(path.Split('/').Last()));
            var ok=data is not null&&!Fail;return Task.FromResult(new HttpResponseMessage(ok?HttpStatusCode.OK:HttpStatusCode.BadGateway){Content=new StringContent(new JsonObject{["isSuccess"]=ok,["data"]=data?.DeepClone()}.ToJsonString(),Encoding.UTF8,"application/json")});
        }
    }
}
