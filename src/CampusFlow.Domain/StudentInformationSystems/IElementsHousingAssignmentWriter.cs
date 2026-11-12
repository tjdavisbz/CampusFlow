using System.Collections.Generic;
using CampusFlow.Housing;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IElementsHousingAssignmentWriter
{
    void EnsureWriteEnabled();
    Task UpdateCapacitiesAsync(int periodId, IReadOnlyList<HousingCapacityChange> changes,
        CancellationToken cancellationToken = default);
    Task VerifyRoomsAsync(int periodId, IReadOnlyList<HousingWorkspaceRoom> expected,
        CancellationToken cancellationToken = default);
    Task<(string Building, string Room)?> GetAssignedRoomAsync(int studentUid, int periodId,
        CancellationToken cancellationToken = default);
    Task DeallocateAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default);
    Task<List<HousingHistoricalAssignment>> GetHistoricalAssignmentsAsync(int periodId, IReadOnlyCollection<int> studentUids,
        CancellationToken cancellationToken = default);
    Task ClearHistoricalAssignmentAsync(HousingHistoricalAssignment reviewed,
        CancellationToken cancellationToken = default);
    Task AssignAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default);
}
