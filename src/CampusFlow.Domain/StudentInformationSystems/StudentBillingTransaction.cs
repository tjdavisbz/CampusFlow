using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentBillingTransaction(
    StudentInformationSystemProvider Provider,
    string ExternalTransactionId,
    string? ExternalTermId,
    string TermCode,
    string TermName,
    DateTime TransactionDate,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal BalanceChange,
    bool IsPending,
    bool IsVoided);
