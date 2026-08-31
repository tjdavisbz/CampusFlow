using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemTermLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<StudentInformationSystemTerm?> GetCurrentTermAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentInformationSystemTerm>> GetTermsAsync(
        CancellationToken cancellationToken = default);
}
