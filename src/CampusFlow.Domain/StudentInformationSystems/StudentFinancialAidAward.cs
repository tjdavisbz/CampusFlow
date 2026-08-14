using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentFinancialAidAward(
    StudentInformationSystemProvider Provider,
    string ExternalAwardId,
    string ExternalTermId,
    string TermCode,
    string TermName,
    DateTime? AwardDate,
    string AwardType,
    string AwardStatus,
    string Description,
    decimal Amount,
    bool IsSentToBilling,
    int? StudentAccepted,
    DateTime? StudentAcceptedTime);
