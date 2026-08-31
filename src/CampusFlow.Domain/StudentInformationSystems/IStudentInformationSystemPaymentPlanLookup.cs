using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemPaymentPlanLookup
{
    StudentInformationSystemProvider Provider { get; }
    Task<StudentPaymentPlanContext?> GetPaymentPlanContextAsync(string externalStudentId, string externalTermId, CancellationToken cancellationToken = default);
}
