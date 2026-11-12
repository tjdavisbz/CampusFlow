using System;

namespace CampusFlow.Housing;

public sealed record HousingHistoricalAssignment(
    int AssignmentId, int StudentUid, int PeriodId, int PeriodRoomId, int PropertyId, int OriginalRoomId,
    string Building, string Room, bool AssignmentDeleted, bool RoomDeleted, bool PropertyDeleted,
    decimal? RoomRate, decimal? PerPersonRate)
{
    public void EnsureRecoverable()
    {
        if (AssignmentId <= 0 || StudentUid <= 0 || PeriodId <= 0 || PeriodRoomId <= 0 || PropertyId <= 0 ||
            OriginalRoomId <= 0 || string.IsNullOrWhiteSpace(Room) ||
            !AssignmentDeleted || !RoomDeleted || !PropertyDeleted ||
            !StudentHousingEligibility.IsCommuterBuilding(Building) ||
            RoomRate.GetValueOrDefault() != 0 || PerPersonRate.GetValueOrDefault() != 0)
            throw new ArgumentException("Historical recovery requires a deleted, uncharged commuter assignment, room and property. Review other history in Elements.");
    }
}
