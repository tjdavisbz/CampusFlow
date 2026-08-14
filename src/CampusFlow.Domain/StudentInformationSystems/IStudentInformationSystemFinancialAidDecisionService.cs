using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public interface IStudentInformationSystemFinancialAidDecisionService
{
    StudentInformationSystemProvider Provider { get; }
    Task<IReadOnlyDictionary<string, int?>> GetDecisionsAsync(
        string externalStudentId, CancellationToken cancellationToken = default);
    Task SubmitDecisionAsync(string externalStudentId, string externalAwardId,
        StudentFinancialAidDecision decision, CancellationToken cancellationToken = default);
}

public enum StudentFinancialAidDecision
{
    Decline = 0,
    Accept = 1
}
