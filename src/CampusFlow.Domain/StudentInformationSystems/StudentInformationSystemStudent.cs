namespace CampusFlow.StudentInformationSystems;

public sealed record StudentInformationSystemStudent(
    StudentInformationSystemProvider Provider,
    string ExternalStudentId,
    string StudentId,
    string Email,
    string FirstName,
    string? PreferredName,
    string LastName);
