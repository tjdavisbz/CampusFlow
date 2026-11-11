using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

// Recovery is deliberately limited to additive commuter assignments. Residential charges,
// moves, removals, capacity edits and unrelated live changes require a separate review.
public static class HousingCommuterRecovery
{
    public static (List<int> Added, List<int> Remaining) Review(
        List<HousingWorkspaceRoom> before, List<HousingWorkspaceRoom> desired,
        List<HousingWorkspaceRoom> current)
    {
        HousingWorkspacePlanner.Compare(before, desired);
        HousingWorkspacePlanner.Compare(before, current, requireCapacityCompliance: false);
        foreach (var room in before)
        {
            var next = desired.Single(x => x.Id == room.Id);
            var live = current.Single(x => x.Id == room.Id);
            if (next.Capacity != room.Capacity || live.Capacity != room.Capacity ||
                next.Name != room.Name || next.Building != room.Building ||
                live.Name != room.Name || live.Building != room.Building)
                throw new ArgumentException("Recovery requires unchanged room identities and capacities.");
        }
        var old = Assignments(before);
        var planned = Assignments(desired);
        var actual = Assignments(current);
        foreach (var (id, row) in old)
            if (!planned.TryGetValue(id, out var next) || next.Room.Id != row.Room.Id ||
                next.Occupant.Rate != row.Occupant.Rate ||
                next.Occupant.IntentionalSingle != row.Occupant.IntentionalSingle ||
                !actual.TryGetValue(id, out var live) || live.Room.Id != row.Room.Id)
                throw new ArgumentException("Recovery cannot include moves, removals, rate changes or changed existing assignments.");
        var added = planned.Keys.Except(old.Keys).Order().ToList();
        if (added.Count == 0 || added.Any(id =>
                !IsCommuter(planned[id].Room.Building) || planned[id].Occupant.IntentionalSingle ||
                planned[id].Occupant.Rate != "Standard"))
            throw new ArgumentException("This recovery supports only added commuter assignments with no housing charge.");
        foreach (var (id, row) in actual)
            if (!planned.TryGetValue(id, out var next) || next.Room.Id != row.Room.Id)
                throw new ArgumentException("Elements contains an unexpected assignment. The saved plan was preserved; resolve the conflict before recovery.");
        return (added, added.Where(id => !actual.ContainsKey(id)).ToList());
    }

    private static bool IsCommuter(string building) =>
        string.Equals(building.Trim(), "Commuter", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(building.Trim(), "Commuter Dorm", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<int, (HousingWorkspaceRoom Room, HousingWorkspaceOccupant Occupant)>
        Assignments(List<HousingWorkspaceRoom> rooms) => rooms
            .SelectMany(room => room.Occupants.Select(occupant => (Room: room, Occupant: occupant)))
            .ToDictionary(x => x.Occupant.StudentUid);
}
