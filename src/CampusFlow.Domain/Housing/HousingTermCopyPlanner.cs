using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

/// <summary>Creates a review-only draft from another period without changing Elements.</summary>
public static class HousingTermCopyPlanner
{
    public static List<HousingWorkspaceRoom> Copy(List<HousingWorkspaceRoom> source,
        List<HousingWorkspaceRoom> destination, List<HousingWorkspaceRoom>? appliedSource = null)
    {
        if (source.Count == 0 || destination.Count == 0)
            throw new ArgumentException("Both housing periods need configured rooms before copying.");
        if (destination.Any(room => room.Occupants.Count > 0))
            throw new ArgumentException("The destination period already has assignments. Review them instead of copying over them.");
        var sourceOccupants = source.SelectMany(room => room.Occupants).ToList();
        if (sourceOccupants.Count == 0)
            throw new ArgumentException("The source period has no assignments to copy.");
        if (sourceOccupants.GroupBy(x => x.StudentUid).Any(x => x.Count() > 1))
            throw new ArgumentException("The source period has duplicate student assignments.");

        static string Key(HousingWorkspaceRoom room) =>
            $"{room.Building.Trim().ToUpperInvariant()}\u001f{room.Name.Trim().ToUpperInvariant()}";
        var duplicateDestinations = destination.GroupBy(Key).Where(group => group.Count() > 1).ToList();
        if (duplicateDestinations.Count > 0)
            throw new ArgumentException("Destination room names are ambiguous. Correct the room setup before copying.");
        var destinationByRoom = destination.ToDictionary(Key);
        var metadata = (appliedSource ?? []).SelectMany(room => room.Occupants)
            .GroupBy(x => x.StudentUid).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var copied = destination.Select(room => new HousingWorkspaceRoom
        {
            Id = room.Id, Building = room.Building, Name = room.Name, Capacity = room.Capacity
        }).ToList();
        var copiedByRoom = copied.ToDictionary(Key);
        foreach (var sourceRoom in source.Where(room => room.Occupants.Count > 0))
        {
            if (!destinationByRoom.ContainsKey(Key(sourceRoom)))
                throw new ArgumentException($"No destination room matches {sourceRoom.Building} / {sourceRoom.Name}.");
            var target = copiedByRoom[Key(sourceRoom)];
            if (sourceRoom.Occupants.Count > target.Capacity)
                throw new ArgumentException($"The destination {target.Building} / {target.Name} has insufficient capacity.");
            foreach (var occupant in sourceRoom.Occupants)
            {
                metadata.TryGetValue(occupant.StudentUid, out var prior);
                target.Occupants.Add(new HousingWorkspaceOccupant
                {
                    StudentUid = occupant.StudentUid, Name = occupant.Name,
                    IntentionalSingle = prior?.IntentionalSingle ?? false,
                    Rate = prior?.Rate ?? "Unreviewed", Notes = ""
                });
            }
        }
        HousingWorkspacePlanner.Compare(destination, copied);
        return copied;
    }
}
