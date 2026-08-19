using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemAdvisorLookup
{
    Task<AdvisorLookupResult?> FindAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdvisorLookupResult>> SearchAsync(string search,
        CancellationToken cancellationToken = default);
}
