using System;
using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Housing.Tests;

public class HousingHistoricalAssignmentTests
{
    private static HousingHistoricalAssignment Row() => new(1, 10, 2, 100, 20, 30,
        "Commuter", "1", true, true, true, null, null);

    [Fact] public void Deleted_commuter_history_with_empty_legacy_rates_is_supported() => Row().EnsureRecoverable();

    [Theory]
    [InlineData("active-assignment")]
    [InlineData("active-room")]
    [InlineData("active-property")]
    [InlineData("residential")]
    [InlineData("room-rate")]
    [InlineData("person-rate")]
    [InlineData("identity")]
    public void Active_noncommuter_priced_or_unidentified_history_is_not_automatically_cleared(string change)
    {
        var row = change switch
        {
            "active-assignment" => Row() with { AssignmentDeleted = false },
            "active-room" => Row() with { RoomDeleted = false },
            "active-property" => Row() with { PropertyDeleted = false },
            "residential" => Row() with { Building = "Bridges" },
            "room-rate" => Row() with { RoomRate = 1 },
            "person-rate" => Row() with { PerPersonRate = 1 },
            _ => Row() with { OriginalRoomId = 0 }
        };
        Assert.Throws<ArgumentException>(row.EnsureRecoverable);
    }
}
