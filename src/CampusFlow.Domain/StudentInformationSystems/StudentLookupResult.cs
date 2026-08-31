namespace CampusFlow.StudentInformationSystems;

public enum StudentLookupStatus
{
    NotFound = 0,
    Matched = 1,
    Ambiguous = 2
}

public sealed record StudentLookupResult(
    StudentLookupStatus Status,
    StudentInformationSystemStudent? Student = null)
{
    public static StudentLookupResult NotFound() => new(StudentLookupStatus.NotFound);

    public static StudentLookupResult Matched(StudentInformationSystemStudent student) =>
        new(StudentLookupStatus.Matched, student);

    public static StudentLookupResult Ambiguous() => new(StudentLookupStatus.Ambiguous);
}
