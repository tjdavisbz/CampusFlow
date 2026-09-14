using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

// A complete period snapshot is required: UI filters never define the scope of removals.
public static class HousingWorkspacePlanner
{
    public static List<HousingWorkspaceChange> Compare(List<HousingWorkspaceRoom> baseline, List<HousingWorkspaceRoom> desired)
    {
        if (baseline.Select(x => x.Id).Distinct().Count() != baseline.Count ||
            desired.Select(x => x.Id).Distinct().Count() != desired.Count ||
            !baseline.Select(x => x.Id).Order().SequenceEqual(desired.Select(x => x.Id).Order()))
            throw new ArgumentException("The complete room list must be retained. Refresh to load new rooms from Elements.");
        if (desired.Any(x => x.Capacity < 0 || x.Capacity > 1000 || x.Occupants.Count > x.Capacity))
            throw new ArgumentException("Room capacity must accommodate all assigned students and be no greater than 1,000.");
        var occupants = desired.SelectMany(x => x.Occupants).ToList();
        if (occupants.Any(x => x.StudentUid <= 0 || x.Notes.Length > 2000 ||
            x.Rate is not ("Unreviewed" or "Standard" or "AIC Triple Room" or "Savell Triple Occupancy")))
            throw new ArgumentException("Check student identifiers, rate selections, and notes (maximum 2,000 characters).");
        if (occupants.GroupBy(x => x.StudentUid).Any(x => x.Count() > 1))
            throw new ArgumentException("A student cannot occupy more than one room in the same housing period.");
        if (baseline.SelectMany(x => x.Occupants).GroupBy(x => x.StudentUid).Any(x => x.Count() > 1))
            throw new ArgumentException("Elements has duplicate student assignments. Resolve them before preparing a sync.");
        var changes = new List<HousingWorkspaceChange>();
        void Add(string action, string detail) => changes.Add(new() { Action = action, Detail = detail });
        foreach (var room in desired)
        {
            var before = baseline.Single(x => x.Id == room.Id);
            if (before.Capacity != room.Capacity)
                Add("Capacity", $"{before.Building} / {before.Name}: {before.Capacity} → {room.Capacity}");
        }
        var oldAssignments = baseline.SelectMany(r => r.Occupants.Select(o => (Room: r, Occupant: o)))
            .ToDictionary(x => x.Occupant.StudentUid);
        var newAssignments = desired.SelectMany(r => r.Occupants.Select(o => (Room: r, Occupant: o)))
            .ToDictionary(x => x.Occupant.StudentUid);
        foreach (var (id, old) in oldAssignments)
            if (!newAssignments.ContainsKey(id)) Add("Remove", $"{old.Occupant.Name}: {old.Room.Building} / {old.Room.Name}");
        foreach (var (id, current) in newAssignments)
        {
            if (!oldAssignments.TryGetValue(id, out var old))
                Add("Assign", $"{current.Occupant.Name}: {current.Room.Building} / {current.Room.Name}");
            else if (old.Room.Id != current.Room.Id)
                Add("Move", $"{current.Occupant.Name}: {old.Room.Building} / {old.Room.Name} → {current.Room.Building} / {current.Room.Name}");
            if (old.Occupant?.Rate != current.Occupant.Rate || old.Occupant?.IntentionalSingle != current.Occupant.IntentionalSingle)
                Add("Rate review", $"{current.Occupant.Name}: {current.Occupant.Rate}; intentional single: {current.Occupant.IntentionalSingle}");
        }
        return changes;
    }
}
