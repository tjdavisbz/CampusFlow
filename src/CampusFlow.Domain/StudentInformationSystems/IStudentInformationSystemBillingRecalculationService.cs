using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemBillingRecalculationService
{
    StudentInformationSystemProvider Provider { get; }
    Task RecalculateAsync(string externalStudentId, string externalTermId, Guid operationId, CancellationToken cancellationToken = default);
}
