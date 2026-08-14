using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemFinancialAidLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<IReadOnlyList<StudentFinancialAidAward>> GetAwardsAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default);
}
