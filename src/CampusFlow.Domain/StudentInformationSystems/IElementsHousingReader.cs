using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;

namespace CampusFlow.StudentInformationSystems;

public interface IElementsHousingReader
{
    Task<StudentHousingEligibility> GetStudentEligibilityAsync(int studentUid, int termCalendarId, CancellationToken cancellationToken = default);
    Task<HousingPeriodSetup> GetPeriodSetupAsync(int periodId, CancellationToken cancellationToken = default);
    Task<List<HousingPeriodOption>> GetPeriodsAsync(CancellationToken cancellationToken = default);
    Task<List<HousingWorkspaceRoom>> GetRoomsAsync(int periodId, CancellationToken cancellationToken = default);
    Task<HousingPricingContext> GetPricingAsync(int periodId, CancellationToken cancellationToken = default);
    Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId, CancellationToken cancellationToken = default);
    Task<StudentHousingAssignment?> GetStudentAssignmentAsync(int studentUid, CancellationToken cancellationToken = default, int? termCalendarId = null);
}
