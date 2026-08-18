using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.Students;

public interface ICurrentStudentView
{
    bool IsImpersonating { get; }
    Task<StudentProfile?> GetProfileAsync(CancellationToken cancellationToken = default);
}

public static class StudentViewClaimTypes
{
    public const string Prefix = "campusflow:student-view:";
    public const string ExternalStudentId = Prefix + "external-id";
    public const string StudentId = Prefix + "student-id";
    public const string Email = Prefix + "email";
    public const string FirstName = Prefix + "first-name";
    public const string PreferredName = Prefix + "preferred-name";
    public const string LastName = Prefix + "last-name";
    public const string Provider = Prefix + "provider";
}
