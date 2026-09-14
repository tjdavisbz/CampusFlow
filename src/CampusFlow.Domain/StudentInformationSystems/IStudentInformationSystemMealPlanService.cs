using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentMealPlanCatalogItem(int MealPlanId, string Name, string Description,
    decimal? Amount, System.DateTime StartDate, System.DateTime EndDate);

public sealed record StudentMealPlanContext(string AttendanceType,
    IReadOnlyList<StudentMealPlanCatalogItem> Catalog,
    IReadOnlyList<StudentMealPlanTerm> Terms,
    int? SelectedTermCalendarId,
    IReadOnlyList<StudentHousingStatusOption> HousingStatuses,
    int? CurrentHousingStatusId);

public sealed record StudentMealPlanTerm(int TermCalendarId, string Name,
    System.DateTime StartDate, System.DateTime EndDate);

public sealed record StudentHousingStatusOption(int Id, string Name);

public interface IStudentInformationSystemMealPlanService
{
    StudentInformationSystemProvider Provider { get; }
    Task<StudentMealPlanContext> GetContextAsync(string externalStudentId, int? termCalendarId = null,
        CancellationToken cancellationToken = default);
    Task UpdateHousingStatusAsync(string externalStudentId, int termCalendarId, int housingStatusId,
        CancellationToken cancellationToken = default);
    Task AssignAsync(string externalStudentId, int mealPlanId,
        CancellationToken cancellationToken = default);
}
