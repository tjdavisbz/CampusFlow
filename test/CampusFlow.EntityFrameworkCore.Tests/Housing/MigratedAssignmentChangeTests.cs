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
        await Assert.ThrowsAsync<HousingPreflightException>(() => Writer(http).DeallocateAsync(11, 2, 31));
        Assert.Empty(http.Writes);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(null, 0, true)]
    [InlineData(0, null, true)]
    [InlineData(null, null, false)]
    [InlineData(1, 0, false)]
    [InlineData(null, 1, false)]
    [InlineData(1, null, false)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    public async Task Conversion_requires_explicit_zero_and_no_nonzero_component(int? roomRate, int? personRate, bool allowed)
    {
        var http = new Handler { RoomRent = roomRate, PersonRent = personRate };
        if (allowed)
        {
            await Writer(http).DeallocateAsync(11, 2, 31);
            Assert.Equal(new[] { "assign", "deallocate" }, http.Writes);
        }
        else
        {
            var error = await Assert.ThrowsAsync<HousingPreflightException>(() => Writer(http).DeallocateAsync(11, 2, 31));
            Assert.Equal("rent-setup", HousingApplyFailure.Code(error));
            Assert.Empty(http.Writes);
        }
    }

    [Fact]
    public async Task Missing_rate_field_is_not_a_verified_zero()
    {
        var http = new Handler { MissingRoomRate = true };
        await Assert.ThrowsAsync<HousingPreflightException>(() => Writer(http).DeallocateAsync(11, 2, 31));
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
        return new(HousingCapacityWriterTests.Config(), cache, new HttpClient(http), historicalAssignments: (_,_,_) => Task.FromResult(new List<HousingHistoricalAssignment>()), migratedHousing: source,
            currentAssignmentExists: (_,_,_) => Task.FromResult(http.Assigned));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Assigned, EverAssigned, MissingRoomRate; public decimal Rent;
        public decimal? RoomRent = 0, PersonRent = 0; public List<string> Writes = [];
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
            else
            {
                data = """{"housingPeriodID":2,"housingPeriodPropertyID":21,"propertyName":"Savell Hall","rooms":[{"housingPeriodRoomID":31,"housingPeriodID":2,"roomName":"101","occupancy":2,"roomRate":ROOM_RENT,"perPersonRate":PERSON_RENT}]}""";
                if (MissingRoomRate) data = data.Replace("\"roomRate\":ROOM_RENT,", "");
                data = data.Replace("ROOM_RENT", (Rent != 0 ? Rent : RoomRent)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")
                    .Replace("PERSON_RENT", PersonRent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"data\":" + data + "}", Encoding.UTF8, "application/json") });
        }
    }
}
