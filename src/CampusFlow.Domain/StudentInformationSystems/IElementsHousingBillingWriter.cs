using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;

namespace CampusFlow.StudentInformationSystems;

public interface IElementsHousingBillingWriter
{
    Task VerifyCanReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyCollection<int> housingDocumentIds, CancellationToken cancellationToken = default);
    Task ReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        CancellationToken cancellationToken = default);
}
