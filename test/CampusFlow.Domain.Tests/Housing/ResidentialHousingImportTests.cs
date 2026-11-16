using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Housing;

public class ResidentialHousingImportTests
{
    static ResidentialHousingImport Import() => new() { PeriodId = 2, TermCalendarId = 269,
        PeriodName = "Spring 2027", Assignments = [new(100, 20, 10, 30, "Guynes Hall", "101", "Test Resident")] };
    static List<HousingWorkspaceRoom> Rooms() => [new() { Id = 20, Building = "Guynes Hall", Name = "101", Capacity = 2 },
        new() { Id = 21, Building = "Commuter", Name = "1", Capacity = 10,
            Occupants = [new() { StudentUid = 200, Name = "Existing Commuter", Rate = "Standard", Notes = "Retain me" }] }];

    [Fact] public void Merge_adds_residents_without_touching_existing_commuters_or_room_settings()
    {
        var rooms = Rooms(); var commuter = rooms[1].Occupants[0];
        Import().MergeInto(2, rooms);
        Assert.Equal(100, rooms[0].Occupants.Single().StudentUid);
        Assert.Equal("Unreviewed", rooms[0].Occupants[0].Rate);
        Assert.Same(commuter, rooms[1].Occupants.Single()); Assert.Equal("Retain me", commuter.Notes);
        Assert.Equal(2, rooms[0].Capacity);
    }
    [Fact] public void Newer_assignments_and_metadata_win_and_repeating_merge_does_not_duplicate()
    {
        var rooms = Rooms(); var newer = new HousingWorkspaceOccupant { StudentUid = 100, Name = "Updated Name", Rate = "Single" };
        rooms[1].Occupants.Add(newer); var import = Import();
        import.MergeInto(2, rooms); import.MergeInto(2, rooms);
        Assert.Empty(rooms[0].Occupants); Assert.Same(newer, rooms[1].Occupants.Single(x => x.StudentUid == 100));
    }
    [Fact] public void Imported_commuters_are_rejected()
    {
        var import = Import(); import.Assignments[0] = import.Assignments[0] with { Building = " Commuter " };
        Assert.Throws<InvalidOperationException>(import.Validate);
    }
    [Fact] public void Duplicate_students_and_legacy_assignments_are_rejected()
    {
        var import = Import(); import.Assignments.Add(import.Assignments[0]);
        Assert.Throws<InvalidOperationException>(import.Validate);
    }
    [Theory] [InlineData("Guynes Hall", "999")] [InlineData("Another Hall", "101")]
    public void A_reused_room_id_cannot_silently_change_the_import(string building, string name)
    {
        var rooms = Rooms(); rooms[0].Building = building; rooms[0].Name = name;
        Assert.Throws<InvalidOperationException>(() => Import().MergeInto(2, rooms));
        Assert.Empty(rooms[0].Occupants);
    }
    [Fact] public void All_mappings_are_validated_before_modifying_the_supplied_snapshot()
    {
        var import = Import(); import.Assignments.Add(new(101, 999, 11, 31, "Guynes Hall", "102", "Missing Room"));
        var rooms = Rooms(); Assert.Throws<InvalidOperationException>(() => import.MergeInto(2, rooms));
        Assert.Empty(rooms[0].Occupants);
    }
    [Fact] public void The_import_cannot_cross_periods()
    {
        var rooms = Rooms(); Assert.Throws<InvalidOperationException>(() => Import().MergeInto(1, rooms));
        Assert.Empty(rooms[0].Occupants);
    }
}
