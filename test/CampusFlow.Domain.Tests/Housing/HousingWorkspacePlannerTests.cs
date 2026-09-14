using System;
using System.Collections.Generic;
using CampusFlow.Housing;
using Shouldly;
using Xunit;

namespace CampusFlow.Housing;

public class HousingWorkspacePlannerTests
{
    private static HousingWorkspaceRoom Room(int id, params int[] students) => new()
    {
        Id = id, Name = $"Room {id}", Building = "Hall", Capacity = 2,
        Occupants = new List<int>(students).ConvertAll(x => new HousingWorkspaceOccupant { StudentUid = x, Name = $"Student {x}" })
    };
    [Fact]
    public void Unchanged_snapshot_has_no_changes() => HousingWorkspacePlanner.Compare([Room(1, 1)], [Room(1, 1)]).ShouldBeEmpty();
    [Fact]
    public void Moves_are_one_operation_not_remove_and_add()
    {
        var result = HousingWorkspacePlanner.Compare([Room(1, 1), Room(2)], [Room(1), Room(2, 1)]);
        result.Count.ShouldBe(1); result[0].Action.ShouldBe("Move");
    }
    [Fact]
    public void Missing_room_is_not_treated_as_permission_to_delete_occupants()
        => Should.Throw<ArgumentException>(() => HousingWorkspacePlanner.Compare([Room(1), Room(2, 1)], [Room(1)]));
    [Fact]
    public void Duplicate_student_is_rejected()
        => Should.Throw<ArgumentException>(() => HousingWorkspacePlanner.Compare([Room(1), Room(2)], [Room(1, 1), Room(2, 1)]));
    [Fact]
    public void Over_capacity_is_rejected()
        => Should.Throw<ArgumentException>(() => HousingWorkspacePlanner.Compare([Room(1)], [Room(1, 1, 2, 3)]));
    [Fact]
    public void Vacancy_does_not_create_a_single_room_surcharge()
    {
        var result = HousingWorkspacePlanner.Compare([Room(1, 1, 2)], [Room(1, 1)]);
        result.Count.ShouldBe(1); result[0].Action.ShouldBe("Remove");
    }
    [Fact]
    public void Explicit_single_is_flagged_for_rate_review()
    {
        var room = Room(1, 1); room.Occupants[0].IntentionalSingle = true;
        HousingWorkspacePlanner.Compare([Room(1, 1)], [room])[0].Action.ShouldBe("Rate review");
    }
    [Fact]
    public void Capacity_change_is_explicit()
    {
        var room = Room(1); room.Capacity = 3;
        HousingWorkspacePlanner.Compare([Room(1)], [room])[0].Action.ShouldBe("Capacity");
    }
}
