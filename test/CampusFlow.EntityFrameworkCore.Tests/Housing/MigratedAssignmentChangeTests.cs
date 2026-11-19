using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public class MigratedAssignmentChangeTests
{
    [Fact]
    public async Task Migrated_baseline_is_verified_and_removal_creates_a_current_catalog_deallocation()
    {
        var http = new Handler(); var writer = Writer(http);
        Assert.Equal(("Savell Hall", "101"), await writer.GetEffectiveAssignedRoomAsync(11, 2));
        await writer.DeallocateAsync(11, 2, 31);
        Assert.Null(await writer.GetEffectiveAssignedRoomAsync(11, 2));
        Assert.Equal(new[] { "assign", "deallocate" }, http.Writes);
    }

    [Fact]
    public async Task A_changed_migration_room_blocks_conversion_without_writes()
    {
        var http = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(http).DeallocateAsync(11, 2, 32));
        Assert.Empty(http.Writes);
    }

    [Fact]
    public async Task Nonzero_vendor_rent_blocks_conversion_before_assignment()
    {
        var http = new Handler { Rent = 100 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(http).DeallocateAsync(11, 2, 31));
        Assert.Empty(http.Writes);
    }

    private static ElementsHousingAssignmentWriter Writer(Handler http)
    {
        var source = Substitute.For<IElementsMigratedHousingReader>();
        source.ReadForPeriodAsync(2, Arg.Any<CancellationToken>()).Returns(_ => new ResidentialHousingImport {
            PeriodId = 2, TermCalendarId = 269, PeriodName = "Spring 2027",
            Assignments = http.EverAssigned ? [] : [new(11,31,101,201,"Savell Hall","101","Test resident")]
        });
        var cache = new MemoryCache(new MemoryCacheOptions()); cache.Set("ThesisElements:Housing:IntegrationToken", "test");
        return new(HousingCapacityWriterTests.Config(), cache, new HttpClient(http), migratedHousing: source,
            currentAssignmentExists: (_,_,_) => Task.FromResult(http.Assigned));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Assigned, EverAssigned; public decimal Rent; public List<string> Writes = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; string data;
            if (request.Method == HttpMethod.Post)
            {
                var action = path.EndsWith("/deallocate") ? "deallocate" : "assign";
                Writes.Add(action); Assigned = action == "assign"; EverAssigned = true; data = "true";
            }
            else if (path.Contains("/get-assigned-rooms/"))
                data = Assigned ? """[{"housingPeriodID":2,"housingPeriodPropertyID":21,"building":"Savell Hall","roomName":"101"}]""" : "[]";
            else if (path.Contains("/search/")) data = """[{"housingPeriodID":2,"housingPeriodPropertyID":21}]""";
            else data = """{"housingPeriodID":2,"housingPeriodPropertyID":21,"propertyName":"Savell Hall","rooms":[{"housingPeriodRoomID":31,"housingPeriodID":2,"roomName":"101","occupancy":2,"roomRate":RENT,"perPersonRate":0}]}""".Replace("RENT", Rent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"data\":" + data + "}", Encoding.UTF8, "application/json") });
        }
    }
}
