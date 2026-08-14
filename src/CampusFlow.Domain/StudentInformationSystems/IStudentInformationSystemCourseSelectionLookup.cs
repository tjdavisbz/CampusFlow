using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemCourseSelectionLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<CourseSelectionContext?> GetContextAsync(
        string externalStudentId,
        string externalTermId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseSelectionContext>> GetEligibleContextsAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseSelectionOffering>> GetAvailableOfferingsAsync(
        string externalTermId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseSelectionRegistration>> GetRegistrationsAsync(
        string externalStudentId,
        string externalTermId,
        CancellationToken cancellationToken = default);

    Task<bool> HasNonWithdrawnCourseAttemptAsync(
        string externalStudentId,
        string department,
        string courseCode,
        string courseType,
        CancellationToken cancellationToken = default);
}
