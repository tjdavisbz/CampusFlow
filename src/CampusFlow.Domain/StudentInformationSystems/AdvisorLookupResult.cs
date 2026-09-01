namespace CampusFlow.StudentInformationSystems;

public sealed record AdvisorLookupResult(
    string ExternalAdvisorId,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    string DisplayName,
    bool CanViewAll);
