using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.StudentInformationSystems;
public sealed record StudentMealPlanAssignment(int AssignmentId,int MealPlanId,int TermCalendarId,string Name,decimal? Amount, System.DateTime? StartDate=null, System.DateTime? EndDate=null);
public interface IStudentInformationSystemMealPlanRemovalService
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<StudentMealPlanAssignment>> GetAssignmentsAsync(string externalStudentId,CancellationToken cancellationToken=default);
    Task RemoveAsync(string externalStudentId,int termId,int assignmentId,int mealPlanId,CancellationToken cancellationToken=default);
}
