using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Http;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

public sealed class CurrentStudentView : ICurrentStudentView
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly IRepository<StaffStudentLink, Guid> _staffLinks;
    private readonly FacultyPortalReader _facultyReader;

    public CurrentStudentView(
        IHttpContextAccessor httpContextAccessor, ICurrentUser currentUser, ICurrentTenant currentTenant,
        IRepository<StudentProfile, Guid> profiles,
        IRepository<StaffStudentLink, Guid> staffLinks,
        FacultyPortalReader facultyReader)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _profiles = profiles;
        _staffLinks = staffLinks;
        _facultyReader = facultyReader;
    }

    public bool IsImpersonating => FindClaim(StudentViewClaimTypes.ExternalStudentId) is not null;

    public async Task<StudentProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!IsImpersonating)
        {
            if (!_currentUser.Id.HasValue) return null;
            var staffLink = await _staffLinks.FindAsync(x => x.StaffUserId == _currentUser.Id.Value,
                cancellationToken: cancellationToken);
            if (staffLink is not null)
            {
                var email = _currentUser.Email;
                if (string.IsNullOrWhiteSpace(email) || StudentOnlyAccountAccess.IsNelsonStudentMailbox(email) ||
                    !await _facultyReader.HasActiveStaffIdentityAsync(email, cancellationToken))
                    return null;
                return await _profiles.FindAsync(staffLink.StudentProfileId, cancellationToken: cancellationToken);
            }
            return await _profiles.FindAsync(x => x.UserId == _currentUser.Id.Value,
                cancellationToken: cancellationToken);
        }

        var externalId = FindClaim(StudentViewClaimTypes.ExternalStudentId)!;
        var provider = Enum.Parse<StudentInformationSystemProvider>(FindClaim(StudentViewClaimTypes.Provider)!);
        var existing = await _profiles.FindAsync(
            x => x.Provider == provider && x.ExternalStudentId == externalId,
            cancellationToken: cancellationToken);
        if (existing is not null) return existing;

        var student = new StudentInformationSystemStudent(
            provider, externalId, FindClaim(StudentViewClaimTypes.StudentId)!,
            FindClaim(StudentViewClaimTypes.Email) ?? string.Empty,
            FindClaim(StudentViewClaimTypes.FirstName)!,
            FindClaim(StudentViewClaimTypes.PreferredName), FindClaim(StudentViewClaimTypes.LastName)!);
        return new StudentProfile(Guid.Empty, _currentTenant.Id, Guid.Empty, student);
    }

    private string? FindClaim(string type) =>
        _httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(x => x.Type == type)?.Value;
}
