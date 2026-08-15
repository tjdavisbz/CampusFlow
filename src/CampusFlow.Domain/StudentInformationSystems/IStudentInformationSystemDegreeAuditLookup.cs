using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemDegreeAuditLookup
{
    StudentInformationSystemProvider Provider { get; }

    Task<IReadOnlyList<StudentDegreeAuditSummary>> GetAuditsAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default);

    Task<StudentDegreeAuditDetail?> GetAuditAsync(
        string externalStudentId,
        int revisionTermId,
        int auditDegreeId,
        int auditProgramId,
        CancellationToken cancellationToken = default);

    Task RefreshAuditAsync(
        string externalStudentId,
        int revisionTermId,
        int auditDegreeId,
        int auditProgramId,
        CancellationToken cancellationToken = default);
}
