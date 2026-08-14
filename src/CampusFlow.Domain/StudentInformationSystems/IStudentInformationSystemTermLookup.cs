using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemTermLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<StudentInformationSystemTerm?> GetCurrentTermAsync(
        CancellationToken cancellationToken = default);
}
