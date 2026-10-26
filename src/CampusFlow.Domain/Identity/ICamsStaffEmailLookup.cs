using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.Identity;

/// <summary>Normalized email addresses present on the staff side of Elements.</summary>
public interface ICamsStaffEmailLookup
{
    Task<IReadOnlyCollection<string>> GetNormalizedEmailsAsync(CancellationToken cancellationToken = default);
}
