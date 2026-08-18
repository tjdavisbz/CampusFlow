using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemStudentLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<StudentLookupResult> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentInformationSystemStudent>> SearchAsync(
        string query,
        int maximumResults = 20,
        CancellationToken cancellationToken = default);
}
