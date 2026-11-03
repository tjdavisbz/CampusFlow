using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemStudentNotesService
{
    Task EnsureSchedulerCommentAsync(string externalStudentId, string termName, string comment,
        DateTime submittedAt, CancellationToken cancellationToken = default);
}
