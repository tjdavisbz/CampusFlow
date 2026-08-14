using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemAdvisorLookup
{
    Task<AdvisorLookupResult?> FindAsync(string email, CancellationToken cancellationToken = default);
}
