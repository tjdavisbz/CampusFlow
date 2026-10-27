using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.Bibliu;

public sealed record BibliuExportRequest(int BillingTermId, int BibliuTermId, string StoreNumber,
    IReadOnlyList<string> ExpandedTermNames, IReadOnlyList<string> ApprovedStudentIds);
public interface IBibliuExportReader
{
    Task<IReadOnlyList<Dictionary<string, string?>>> ReadAsync(string kind, BibliuExportRequest request, CancellationToken cancellationToken);
}
