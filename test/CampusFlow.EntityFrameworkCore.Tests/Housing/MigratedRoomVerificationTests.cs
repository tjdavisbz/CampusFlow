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
using NSubstitute;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public class MigratedRoomVerificationTests
{
    private readonly IElementsMigratedHousingReader _source=Substitute.For<IElementsMigratedHousingReader>();
    private readonly Handler _http=new();
    private readonly ResidentialHousingImport _migration=new()
    {
        PeriodId=2,TermCalendarId=269,PeriodName="Spring 2027",
        Assignments=[new(11,31,101,201,"Savell Hall","101","Migrated roommate")]
    };
    private readonly List<HousingWorkspaceRoom> _rooms=[new()
    {
        Id=31,Name="101",Building="Savell Hall",Capacity=2,
        Occupants=[new(){StudentUid=10,Name="Current resident"},new(){StudentUid=11,Name="Migrated roommate"}]
    }];
    private ElementsHousingAssignmentWriter Writer(bool currentExists=false)
    {
        _source.ReadForPeriodAsync(2,Arg.Any<CancellationToken>()).Returns(_migration);
        var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:Housing:IntegrationToken","test");
        return new(HousingCapacityWriterTests.Config(),cache,new HttpClient(_http),
            migratedHousing:_source,currentAssignmentExists:(_,_,_)=>Task.FromResult(currentExists));
    }
    [Fact] public async Task An_Elements_migrated_roommate_does_not_block_a_current_resident_removal_preflight()
    {
        var writer=Writer();await writer.VerifyRoomsAsync(2,_rooms);
        // Verification performs only reads and never fabricates an actionable modern assignment.
        Assert.Null(await writer.GetAssignedRoomAsync(11,2));
        Assert.All(_http.Methods,x=>Assert.Equal(HttpMethod.Get,x));
        await _source.Received(1).ReadForPeriodAsync(2,Arg.Any<CancellationToken>());
    }
    [Theory]
    [InlineData("room-id")][InlineData("room-name")][InlineData("building")]
    [InlineData("period")][InlineData("term")][InlineData("missing")][InlineData("duplicate")]
    public async Task Missing_changed_or_ambiguous_migrated_identity_still_blocks_apply(string change)
    {
        var writer=Writer();var a=_migration.Assignments[0];
        switch(change)
        {
            case "room-id":_migration.Assignments[0]=a with {PeriodRoomId=99};break;
            case "room-name":_migration.Assignments[0]=a with {Room="102"};break;
            case "building":_migration.Assignments[0]=a with {Building="Bridges Hall"};break;
            case "period":_migration.PeriodId=1;break;
            case "term":_migration.TermCalendarId=268;break;
            case "missing":_migration.Assignments.Clear();break;
            case "duplicate":_migration.Assignments.Add(a);break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.VerifyRoomsAsync(2,_rooms));
        Assert.All(_http.Methods,x=>Assert.Equal(HttpMethod.Get,x));
    }
    [Fact] public async Task A_current_StudentLife_row_missing_from_the_API_cannot_be_replaced_by_a_stale_migration()
    { await Assert.ThrowsAsync<InvalidOperationException>(()=>Writer(currentExists:true).VerifyRoomsAsync(2,_rooms)); }
    [Fact] public async Task A_conflicting_live_room_is_never_overridden_by_migrated_data()
    {
        _http.MigratedHasOtherRoom=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Writer().VerifyRoomsAsync(2,_rooms));
        await _source.DidNotReceiveWithAnyArgs().ReadForPeriodAsync(default,default);
    }
    [Fact] public async Task A_changed_live_capacity_still_blocks_verification()
    {
        _rooms[0].Capacity=3;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Writer().VerifyRoomsAsync(2,_rooms));
        await _source.DidNotReceiveWithAnyArgs().ReadForPeriodAsync(default,default);
    }
    [Fact] public async Task Elements_read_failure_never_counts_as_a_confirmed_migrated_roommate()
    {
        var writer=Writer();_source.ReadForPeriodAsync(2,Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResidentialHousingImport?>(new InvalidOperationException("Source unavailable")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.VerifyRoomsAsync(2,_rooms));
    }
    [Fact] public async Task Native_only_rooms_keep_the_existing_live_API_verification()
    {
        _rooms[0].Occupants.RemoveAt(1);await Writer().VerifyRoomsAsync(2,_rooms);
        await _source.DidNotReceiveWithAnyArgs().ReadForPeriodAsync(default,default);
    }
    private sealed class Handler:HttpMessageHandler
    {
        public bool MigratedHasOtherRoom;
        public List<HttpMethod> Methods=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Methods.Add(request.Method);Assert.Equal(HttpMethod.Get,request.Method);
            var path=request.RequestUri!.AbsolutePath;JsonNode data;
            if(path.Contains("/get-assigned-rooms/"))
            {
                var id=int.Parse(path.Split('/').Last());var name=id==10?"101":"102";
                data=id==10||MigratedHasOtherRoom?new JsonArray(new JsonObject
                { ["housingPeriodID"]=2,["housingPeriodPropertyID"]=21,["building"]="Savell Hall",["roomName"]=name }):new JsonArray();
            }
            else if(path.Contains("/search/"))data=new JsonArray(new JsonObject{["housingPeriodID"]=2,["housingPeriodPropertyID"]=21});
            else data=new JsonObject
            {
                ["housingPeriodID"]=2,["housingPeriodPropertyID"]=21,["propertyName"]="Savell Hall",
                ["rooms"]=new JsonArray(new JsonObject{["housingPeriodRoomID"]=31,["housingPeriodID"]=2,["roomName"]="101",["occupancy"]=2},
                    new JsonObject{["housingPeriodRoomID"]=32,["housingPeriodID"]=2,["roomName"]="102",["occupancy"]=2})
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(
                new JsonObject{["isSuccess"]=true,["data"]=data}.ToJsonString(),Encoding.UTF8,"application/json")});
        }
    }
}
