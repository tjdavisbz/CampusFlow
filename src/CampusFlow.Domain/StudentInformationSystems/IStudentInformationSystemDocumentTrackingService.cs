using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentDocumentTrackingRequest(
    string ExternalStudentId,
    string TermName,
    Guid ApprovalId,
    DateTime AcceptedAt);

public interface IStudentInformationSystemDocumentTrackingService
{
    StudentInformationSystemProvider Provider { get; }
    Task<string> CreateApprovedBillAsync(StudentDocumentTrackingRequest request, CancellationToken cancellationToken = default);
    Task<bool> HasImageAsync(string documentTrackingId, CancellationToken cancellationToken = default);
    Task UploadImageAsync(string documentTrackingId, string fileName, byte[] contents, CancellationToken cancellationToken = default);
}
