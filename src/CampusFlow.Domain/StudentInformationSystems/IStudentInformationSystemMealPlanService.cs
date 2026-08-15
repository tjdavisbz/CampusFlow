using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentMealPlanCatalogItem(int MealPlanId, string Name, string Description,
    decimal? Amount, System.DateTime StartDate, System.DateTime EndDate);

public sealed record StudentMealPlanContext(string AttendanceType,
    IReadOnlyList<StudentMealPlanCatalogItem> Catalog);

public interface IStudentInformationSystemMealPlanService
{
    StudentInformationSystemProvider Provider { get; }
    Task<StudentMealPlanContext> GetContextAsync(string externalStudentId,
        CancellationToken cancellationToken = default);
    Task AssignAsync(string externalStudentId, int mealPlanId,
        CancellationToken cancellationToken = default);
}
