using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentPaymentPostingResult(int BatchMasterId, int BillingBatchId);

public interface IStudentInformationSystemPaymentPostingService
{
    StudentInformationSystemProvider Provider { get; }

    Task<StudentPaymentPostingResult> PostAsync(
        string externalStudentId,
        int termCalendarId,
        decimal amount,
        string externalReference,
        bool isTest,
        DateTime transactionDate,
        CancellationToken cancellationToken = default);
}
