using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

public static class HousingBillingRecovery
{
    public static List<int> Review(List<HousingWorkspaceRoom> before, List<HousingWorkspaceRoom> desired,
        List<HousingWorkspaceRoom> current, IReadOnlyList<string> originalOperations)
    {
        HousingWorkspacePlanner.Compare(before, desired);
        HousingWorkspacePlanner.Compare(before, current, requireCapacityCompliance: false);
        foreach (var room in before)
        {
            var planned = desired.Single(x => x.Id == room.Id);
            var actual = current.Single(x => x.Id == room.Id);
            if (planned.Capacity != room.Capacity || actual.Capacity != room.Capacity ||
                planned.Building != room.Building || actual.Building != room.Building ||
                planned.Name != room.Name || actual.Name != room.Name)
                throw new ArgumentException("Billing recovery requires unchanged room identities and capacities.");
        }
        var old = Map(before); var next = Map(desired); var live = Map(current);
        if (next.Count != live.Count || next.Any(x => !live.TryGetValue(x.Key,out var actual) || actual.RoomId != x.Value.RoomId))
            throw new ArgumentException("The live room assignments do not match the complete saved plan. Billing-only recovery cannot change rooms.");
        var changed = old.Keys.Union(next.Keys).Where(id =>
            !old.TryGetValue(id,out var previous) || !next.TryGetValue(id,out var planned) ||
            previous.RoomId != planned.RoomId || previous.Occupant.Rate != planned.Occupant.Rate ||
            previous.Occupant.IntentionalSingle != planned.Occupant.IntentionalSingle).Order().ToList();
        if (changed.Count == 0 || !originalOperations.Any(x => x.StartsWith("Starting charge reconciliation for ",StringComparison.Ordinal)))
            throw new ArgumentException("The saved run has not verified completion of its room changes and entry into billing.");
        foreach (var id in changed.Where(id => !old.TryGetValue(id,out var a) || !next.TryGetValue(id,out var b) || a.RoomId != b.RoomId))
            if (old.ContainsKey(id) && !originalOperations.Contains($"Room removal verified for {id}") ||
                next.ContainsKey(id) && !originalOperations.Contains($"Room assignment verified for {id}"))
                throw new ArgumentException("The saved room operations are incomplete. Billing-only recovery requires a separate assignment review.");
        return changed;
    }

    private static Dictionary<int,(int RoomId,HousingWorkspaceOccupant Occupant)> Map(List<HousingWorkspaceRoom> rooms) =>
        rooms.SelectMany(r=>r.Occupants.Select(o=>(r.Id,Occupant:o))).ToDictionary(x=>x.Occupant.StudentUid,x=>x);
}
