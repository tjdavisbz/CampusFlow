using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentInformationSystemTerm(
    StudentInformationSystemProvider Provider,
    string ExternalTermId,
    string TermCode,
    string DisplayName,
    DateTime StartDate,
    DateTime EndDate);
