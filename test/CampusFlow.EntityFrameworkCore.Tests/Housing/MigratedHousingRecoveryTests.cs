using System;
using System.Collections.Generic;
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

public class MigratedHousingRecoveryTests
{
    private static readonly ImportedResidentialAssignment Migrated = new(11,31,101,201,"Savell Hall","101","Resident");
    private static HousingHistoricalAssignment Old() => new(901,11,2,900,901,201,"Savell Hall","101",true,true,true,5500,2750);

    [Fact]
    public async Task Exact_deleted_residential_row_is_deallocated_before_conversion_without_changing_money()
    {
        var handler = new Handler();
        var writer = Writer(handler);
        await writer.DeallocateAsync(11,2,31);
        Assert.Equal(new[] { "deallocate:900", "assign:31", "deallocate:31" },handler.Writes);
        Assert.Null(await writer.GetEffectiveAssignedRoomAsync(11,2));
    }

    [Theory]
    [InlineData("room")]
    [InlineData("student")]
    [InlineData("period")]
    [InlineData("building")]
    [InlineData("activeAssignment")]
    [InlineData("activeRoom")]
    [InlineData("activeProperty")]
    [InlineData("multiple")]
    public async Task Unrelated_or_ambiguous_history_never_dispatches_a_write(string problem)
    {
        var old = problem switch {
            "room" => Old() with { OriginalRoomId=202 }, "student" => Old() with { StudentUid=12 },
            "period" => Old() with { PeriodId=3 }, "building" => Old() with { Building="Other Hall" },
            "activeAssignment" => Old() with { AssignmentDeleted=false }, "activeRoom" => Old() with { RoomDeleted=false },
            "activeProperty" => Old() with { PropertyDeleted=false }, _ => Old()
        };
        var handler = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(handler,problem=="multiple"?[old,old with { AssignmentId=902 }]:[old]).DeallocateAsync(11,2,31));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task A_billing_change_after_historical_deallocation_stops_conversion_for_review()
    {
        var handler = new Handler { BillingChanged=true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(handler).DeallocateAsync(11,2,31));
        Assert.Equal(new[] { "deallocate:900" },handler.Writes);
    }

    [Fact]
    public async Task A_failed_historical_write_is_not_retried()
    {
        var handler = new Handler { FailHistorical=true };
        await Assert.ThrowsAsync<HousingRequestException>(() => Writer(handler).DeallocateAsync(11,2,31));
        Assert.Equal(new[] { "deallocate:900" },handler.Writes);
    }

    [Fact]
    public async Task A_new_native_assignment_blocks_recovery_before_dispatch()
    {
        var handler = new Handler(); var reads=0;
        var writer = Writer(handler,currentExists:()=>++reads>1);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.DeallocateAsync(11,2,31));
        Assert.Empty(handler.Writes);
    }

    private static ElementsHousingAssignmentWriter Writer(Handler http,List<HousingHistoricalAssignment>? history=null,Func<bool>? currentExists=null)
    {
        var source = Substitute.For<IElementsMigratedHousingReader>();
        source.ReadForPeriodAsync(2,Arg.Any<CancellationToken>()).Returns(_=>new ResidentialHousingImport {
            PeriodId=2,TermCalendarId=269,PeriodName="Spring 2027",Assignments=http.EverAssigned?[]:[Migrated]
        });
        var cache = new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:Housing:IntegrationToken","test");
        return new(HousingCapacityWriterTests.Config(),cache,new HttpClient(http),
            historicalAssignments:(_,_,_)=>Task.FromResult(http.HistoricalRemoved?new List<HousingHistoricalAssignment>():history??[Old()]),
            migratedHousing:source,currentAssignmentExists:(_,_,_)=>Task.FromResult(currentExists?.Invoke()??http.Assigned),
            migrationBillingSnapshot:(_,_)=>Task.FromResult(http.HistoricalRemoved&&http.BillingChanged?"changed":"unchanged"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Assigned,EverAssigned,HistoricalRemoved,BillingChanged,FailHistorical;
        public List<string> Writes=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;string data;
            if(request.Method==HttpMethod.Post)
            {
                var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;var room=body["housingPeriodRoomId"]!.GetValue<int>();
                var action=path.EndsWith("/deallocate")?"deallocate":"assign";Writes.Add(action+":"+room);
                if(room==900)
                {
                    if(FailHistorical)return new(HttpStatusCode.Conflict){Content=new StringContent("{\"isSuccess\":false}")};
                    HistoricalRemoved=true;
                }
                else { Assigned=action=="assign";EverAssigned=true; }
                data="true";
            }
            else if(path.Contains("/get-assigned-rooms/"))data=Assigned?"""[{"housingPeriodID":2,"housingPeriodPropertyID":21,"building":"Savell Hall","roomName":"101"}]""":"[]";
            else if(path.Contains("/search/"))data="""[{"housingPeriodID":2,"housingPeriodPropertyID":21}]""";
            else data="""{"housingPeriodID":2,"housingPeriodPropertyID":21,"propertyName":"Savell Hall","rooms":[{"housingPeriodRoomID":31,"housingPeriodID":2,"originalRoomID":201,"roomName":"101","occupancy":2,"roomRate":null,"perPersonRate":0}]}""";
            return new(HttpStatusCode.OK){Content=new StringContent("{\"isSuccess\":true,\"data\":"+data+"}",Encoding.UTF8,"application/json")};
        }
    }
}
