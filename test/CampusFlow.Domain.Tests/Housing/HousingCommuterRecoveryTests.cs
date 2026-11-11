using System;
using System.Collections.Generic;
using System.Text.Json;
using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Domain.Tests.Housing;

public class HousingCommuterRecoveryTests
{
    private static List<HousingWorkspaceRoom> Rooms() =>
        [new() { Id = 1, Building = "Commuter", Name = "1", Capacity = 10 },
         new() { Id = 2, Building = "Commuter", Name = "2", Capacity = 10 }];
    private static HousingWorkspaceOccupant Student(int id) => new() { StudentUid = id, Rate = "Standard" };
    private static List<HousingWorkspaceRoom> Copy(List<HousingWorkspaceRoom> rooms) =>
        JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(rooms))!;

    [Fact]
    public void Partial_success_is_classified_without_modifying_any_snapshot()
    {
        var before = Rooms(); var desired = Copy(before); var current = Copy(before);
        desired[0].Occupants.Add(Student(10)); desired[1].Occupants.Add(Student(11)); current[0].Occupants.Add(Student(10));
        var json = JsonSerializer.Serialize(new[] { before, desired, current });
        var plan = HousingCommuterRecovery.Review(before, desired, current);
        Assert.Equal(new[] { 10, 11 }, plan.Added); Assert.Equal(new[] { 11 }, plan.Remaining);
        Assert.Equal(json, JsonSerializer.Serialize(new[] { before, desired, current }));
    }

    [Theory]
    [InlineData("new-live-student")][InlineData("wrong-room")][InlineData("capacity")]
    [InlineData("renamed-room")][InlineData("residential")][InlineData("single")]
    [InlineData("special-rate")][InlineData("missing-room")][InlineData("duplicate-student")]
    public void Conflicts_and_non_commuter_operations_are_never_recovered(string scenario)
    {
        var before = Rooms(); var desired = Copy(before); var current = Copy(before);
        desired[0].Occupants.Add(Student(10));
        switch (scenario)
        {
            case "new-live-student": current[1].Occupants.Add(Student(99)); break;
            case "wrong-room": current[1].Occupants.Add(Student(10)); break;
            case "capacity": current[0].Capacity = 9; break;
            case "renamed-room": current[0].Name = "Other"; break;
            case "residential": before[0].Building = desired[0].Building = current[0].Building = "Bridges Hall"; break;
            case "single": desired[0].Occupants[0].IntentionalSingle = true; break;
            case "special-rate": desired[0].Occupants[0].Rate = "AIC Triple Room"; break;
            case "missing-room": current.RemoveAt(1); break;
            case "duplicate-student": current[0].Occupants.Add(Student(10)); current[1].Occupants.Add(Student(10)); break;
        }
        Assert.Throws<ArgumentException>(() => HousingCommuterRecovery.Review(before, desired, current));
    }

    [Theory][InlineData("move")][InlineData("remove")][InlineData("rate")][InlineData("live-removal")]
    public void Existing_students_must_remain_unchanged(string scenario)
    {
        var before = Rooms(); before[0].Occupants.Add(Student(1)); var desired = Copy(before); var current = Copy(before);
        desired[1].Occupants.Add(Student(10));
        switch (scenario)
        {
            case "move": desired[0].Occupants.Clear(); desired[1].Occupants.Add(Student(1)); break;
            case "remove": desired[0].Occupants.Clear(); break;
            case "rate": desired[0].Occupants[0].Rate = "Unreviewed"; break;
            case "live-removal": current[0].Occupants.Clear(); break;
        }
        Assert.Throws<ArgumentException>(() => HousingCommuterRecovery.Review(before, desired, current));
    }

    [Fact]
    public void Partial_apply_cannot_be_discarded_by_refresh()
    {
        var now = DateTime.UtcNow;
        var draft = new HousingAssignmentDraft(Guid.NewGuid(), Guid.NewGuid(), 2, "[]", now);
        new HousingApplyProgress { Status = "Needs review", StartedAt = now.AddHours(-2) }.Save(draft);
        Assert.Throws<InvalidOperationException>(() => HousingApplyProgress.EnsureEditable(draft, now, refreshing: true));
    }
}
