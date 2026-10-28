using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemAwardCodeMaintenance
{
    StudentInformationSystemProvider Provider { get; }
    bool CanWrite { get; }
    Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AwardCodeMismatch>> GetMismatchesAsync(string term, int take,
        CancellationToken cancellationToken = default);
    Task UpdateAsync(AwardCodeMismatch expected, CancellationToken cancellationToken = default);
}
