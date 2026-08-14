using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemBillingLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<IReadOnlyList<StudentBillingTransaction>> GetTransactionsAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default);
}
