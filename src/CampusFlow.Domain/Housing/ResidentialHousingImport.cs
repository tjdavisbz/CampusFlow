using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.Housing;

public sealed record ImportedResidentialAssignment(int StudentUid, int PeriodRoomId, int LegacyAssignmentId,
    int LegacyRoomId, string Building, string Room, string Name);

// A reviewed, one-time copy. It never writes an Elements assignment or billing entry.
public sealed class ResidentialHousingImport
{
    public const string PropertyName = "ResidentialHousingImportV1";
    public int PeriodId { get; set; }
    public int TermCalendarId { get; set; }
    public string PeriodName { get; set; } = "";
    public DateTime ImportedAtUtc { get; set; }
    public string SourceSha256 { get; set; } = "";
    public List<ImportedResidentialAssignment> Assignments { get; set; } = [];

    public void Validate()
    {
        if (PeriodId <= 0 || TermCalendarId <= 0 || string.IsNullOrWhiteSpace(PeriodName) ||
            Assignments.Any(x => x.StudentUid <= 0 || x.PeriodRoomId <= 0 || x.LegacyAssignmentId <= 0 ||
                x.LegacyRoomId <= 0 || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Building) ||
                string.IsNullOrWhiteSpace(x.Room) || StudentHousingEligibility.IsCommuterBuilding(x.Building)) ||
            Assignments.Select(x => x.StudentUid).Distinct().Count() != Assignments.Count ||
            Assignments.Select(x => x.LegacyAssignmentId).Distinct().Count() != Assignments.Count)
            throw new InvalidOperationException("The residential import is incomplete or ambiguous.");
    }

    public void MergeInto(int periodId, List<HousingWorkspaceRoom> rooms)
    {
        Validate();
        if (PeriodId != periodId || rooms.Select(x => x.Id).Distinct().Count() != rooms.Count)
            throw new InvalidOperationException("The residential import belongs to a different housing period.");
        var occupied = rooms.SelectMany(x => x.Occupants).Select(x => x.StudentUid).ToHashSet();
        var additions = new List<(HousingWorkspaceRoom Room, ImportedResidentialAssignment Assignment)>();
        foreach (var assignment in Assignments.Where(x => !occupied.Contains(x.StudentUid)))
        {
            var room = rooms.SingleOrDefault(x => x.Id == assignment.PeriodRoomId);
            if (room is null || !string.Equals(room.Building.Trim(), assignment.Building.Trim(), StringComparison.Ordinal) ||
                !string.Equals(room.Name.Trim(), assignment.Room.Trim(), StringComparison.Ordinal) ||
                StudentHousingEligibility.IsCommuterBuilding(room.Building))
                throw new InvalidOperationException("An imported residential room changed. Review the import before refreshing.");
            additions.Add((room, assignment));
        }
        foreach (var (room, assignment) in additions)
            room.Occupants.Add(new HousingWorkspaceOccupant { StudentUid = assignment.StudentUid, Name = assignment.Name,
                Rate = "Standard" });
    }
}

public interface IElementsMigratedHousingReader
{
    Task<ResidentialHousingImport?> ReadForPeriodAsync(int periodId, CancellationToken cancellationToken = default);
    Task<ResidentialHousingImport?> ReadForTermAsync(int termCalendarId, CancellationToken cancellationToken = default);
}
