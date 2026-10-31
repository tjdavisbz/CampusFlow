using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

// A local StudentProfile is not by itself proof that the account is student-only:
// some staff and faculty are also students. Their exact Elements email must match.
public sealed class StudentOnlyAccountAccess(
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    IRepository<StudentProfile, Guid> profiles,
    IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
    FacultyPortalReader facultyReader,
    ILogger<StudentOnlyAccountAccess> logger) : ITransientDependency
{
    private const string CacheKey = "CampusFlow.StudentOnlyAccount";
    private const string StaffCacheKey = "CampusFlow.StaffAccount";

    public async Task<bool> HasStaffAccessAsync()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.Email) ||
            IsNelsonStudentMailbox(currentUser.Email)) return false;

        var context = httpContextAccessor.HttpContext;
        if (context?.Items.TryGetValue(StaffCacheKey, out var cached) == true && cached is bool result)
            return result;

        var hasStaffAccess = false;
        try
        {
            hasStaffAccess = await facultyReader.HasActiveStaffIdentityAsync(
                currentUser.Email, context?.RequestAborted ?? default);
        }
        catch (Exception exception) when (context?.RequestAborted.IsCancellationRequested != true)
        {
            logger.LogWarning(exception, "Unable to verify staff portal access; denying access.");
        }

        if (context is not null) context.Items[StaffCacheKey] = hasStaffAccess;
        return hasStaffAccess;
    }

    public async Task<bool> IsStudentOnlyAsync()
    {
        if (!currentUser.IsAuthenticated) return false;
        var context = httpContextAccessor.HttpContext;
        if (context?.Items.TryGetValue(CacheKey, out var cached) == true && cached is bool result)
            return result;

        // An authenticated identity without a usable email must never reach a privileged area.
        var studentOnly = string.IsNullOrWhiteSpace(currentUser.Email);
        if (!studentOnly)
        {
            try
            {
                // Student mailboxes are never staff logins, even if the address appears
                // on a staff record. For other addresses, verify staff identity first:
                // a staff member may also be a student, and a student API outage must
                // not lock that staff member out of administration.
                if (IsNelsonStudentMailbox(currentUser.Email!))
                {
                    studentOnly = true;
                }
                else if (await facultyReader.HasStaffOrFacultyIdentityAsync(
                    currentUser.Email!, context?.RequestAborted ?? default))
                {
                    studentOnly = false;
                }
                else
                {
                    var isStudent = currentUser.Id.HasValue &&
                        await profiles.AnyAsync(x => x.UserId == currentUser.Id.Value);
                    if (!isStudent)
                    {
                        var lookup = studentLookups.SingleOrDefault(x =>
                            x.Provider == StudentInformationSystemProvider.ThesisElements);
                        if (lookup is null) throw new InvalidOperationException("Elements student lookup is unavailable.");
                        var match = await lookup.FindByEmailAsync(currentUser.Email!, context?.RequestAborted ?? default);
                        isStudent = match.Status != StudentLookupStatus.NotFound;
                    }
                    studentOnly = isStudent;
                }
            }
            catch (Exception exception) when (context?.RequestAborted.IsCancellationRequested != true)
            {
                logger.LogWarning(exception, "Unable to verify portal account type; denying privileged access.");
                studentOnly = true;
            }
        }

        if (context is not null) context.Items[CacheKey] = studentOnly;
        return studentOnly;
    }

    public static bool IsNelsonStudentMailbox(string email) =>
        email.Trim().EndsWith("@my.nelson.edu", StringComparison.OrdinalIgnoreCase);
}
