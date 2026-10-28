using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;

namespace CampusFlow.StudentInformationSystems;

public interface IElementsHousingReader
{
    Task<List<HousingPeriodOption>> GetPeriodsAsync(CancellationToken cancellationToken = default);
    Task<List<HousingWorkspaceRoom>> GetRoomsAsync(int periodId, CancellationToken cancellationToken = default);
    Task<HousingPricingContext> GetPricingAsync(int periodId, CancellationToken cancellationToken = default);
    Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId, CancellationToken cancellationToken = default);
    Task<StudentHousingAssignment?> GetStudentAssignmentAsync(int studentUid, CancellationToken cancellationToken = default);
}
