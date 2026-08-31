using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemCourseRegistrationService
{
    StudentInformationSystemProvider Provider { get; }

    Task<string> AddUnofficialCourseAsync(
        string externalStudentId,
        string externalTermId,
        string externalOfferingId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task RemoveCourseAsync(
        string externalStudentId,
        string externalTermId,
        string externalOfferingId,
        string externalRegistrationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
