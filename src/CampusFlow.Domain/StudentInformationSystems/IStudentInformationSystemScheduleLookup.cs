using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemScheduleLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<IReadOnlyList<StudentCourseScheduleItem>> GetScheduleAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default);
}
