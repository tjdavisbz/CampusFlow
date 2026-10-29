using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IElementsHousingAssignmentWriter
{
    void EnsureWriteEnabled();
    Task<(string Building, string Room)?> GetAssignedRoomAsync(int studentUid, int periodId,
        CancellationToken cancellationToken = default);
    Task DeallocateAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default);
    Task AssignAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default);
}
