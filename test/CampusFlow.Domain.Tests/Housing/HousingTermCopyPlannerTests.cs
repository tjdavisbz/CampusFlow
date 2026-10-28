using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.Housing;
using Shouldly;
using Xunit;

namespace CampusFlow.Housing;

public class HousingTermCopyPlannerTests
{
    private static HousingWorkspaceRoom Room(int id, int capacity, params int[] students) => new()
    {
        Id = id, Building = "Bridges Hall", Name = "101", Capacity = capacity,
        Occupants = students.Select(uid => new HousingWorkspaceOccupant
        {
            StudentUid = uid, Name = $"Student {uid}", Rate = "Standard"
        }).ToList()
    };

    [Fact]
    public void Copies_applied_choices_into_destination_room_id()
    {
        var source = Room(1, 2, 42);
        var destination = Room(99, 2);
        var applied = Room(1, 2, 42);
        applied.Occupants[0].IntentionalSingle = true;

        var copied = HousingTermCopyPlanner.Copy([source], [destination], [applied]);

        copied.Single().Id.ShouldBe(99);
        copied.Single().Occupants.Single().StudentUid.ShouldBe(42);
        copied.Single().Occupants.Single().IntentionalSingle.ShouldBeTrue();
        copied.Single().Occupants.Single().Rate.ShouldBe("Standard");
        destination.Occupants.ShouldBeEmpty();
    }

    [Fact]
    public void Requires_review_when_applied_choice_is_unavailable()
    {
        var copied = HousingTermCopyPlanner.Copy([Room(1, 2, 42)], [Room(99, 2)]);
        copied.Single().Occupants.Single().Rate.ShouldBe("Unreviewed");
    }

    [Fact]
    public void Refuses_destination_with_assignments()
        => Should.Throw<ArgumentException>(() => HousingTermCopyPlanner.Copy([Room(1, 2, 42)], [Room(99, 2, 50)]));

    [Fact]
    public void Refuses_missing_destination_room()
    {
        var destination = Room(99, 2);
        destination.Name = "102";
        Should.Throw<ArgumentException>(() => HousingTermCopyPlanner.Copy([Room(1, 2, 42)], [destination]));
    }

    [Fact]
    public void Refuses_insufficient_destination_capacity()
        => Should.Throw<ArgumentException>(() => HousingTermCopyPlanner.Copy([Room(1, 2, 42, 43)], [Room(99, 1)]));

    [Fact]
    public void Refuses_duplicate_source_student()
    {
        var second = Room(2, 2, 42);
        second.Name = "102";
        var destinationSecond = Room(100, 2);
        destinationSecond.Name = "102";
        Should.Throw<ArgumentException>(() => HousingTermCopyPlanner.Copy(
            [Room(1, 2, 42), second], [Room(99, 2), destinationSecond]));
    }
}
