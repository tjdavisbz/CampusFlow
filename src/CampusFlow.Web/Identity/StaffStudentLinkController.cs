using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Identity;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace CampusFlow.Web.Identity;

[Route("api/admin/staff-student-links")]
[Authorize(CampusFlowPermissions.Admin.AccessManagement)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffStudentLinkController(
    IRepository<IdentityUser, Guid> users,
    IRepository<StudentProfile, Guid> profiles,
    IRepository<StaffStudentLink, Guid> links,
    FacultyPortalReader facultyReader,
    IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
    IGuidGenerator guidGenerator,
    IUnitOfWorkManager unitOfWorkManager,
    ICurrentTenant currentTenant) : AbpController
{
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetAsync(Guid userId)
    {
        var user = await GetEligibleUserAsync(userId);
        if (user is null) return NotFound();
        var link = await links.FindAsync(x => x.StaffUserId == userId);
        var student = link is null ? null : await profiles.FindAsync(link.StudentProfileId);
        return Ok(new
        {
            staffEmail = user.Email,
            student = student is null ? null : new
            {
                studentId = student.StudentId,
                name = student.DisplayName,
                email = student.Email
            }
        });
    }

    [HttpGet("{userId:guid}/students")]
    public async Task<IActionResult> SearchStudentsAsync(Guid userId, [FromQuery] string? q)
    {
        if (await GetEligibleUserAsync(userId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(Array.Empty<object>());
        var students = await StudentLookup.SearchAsync(q.Trim(), cancellationToken: HttpContext.RequestAborted);
        return Ok(students.Select(student => new
        {
            externalStudentId = student.ExternalStudentId,
            studentId = student.StudentId,
            name = $"{(string.IsNullOrWhiteSpace(student.PreferredName) ? student.FirstName : student.PreferredName)} {student.LastName}".Trim(),
            email = student.Email
        }));
    }

    [HttpPost("{userId:guid}")]
    public async Task<IActionResult> SetAsync(Guid userId, [FromBody] SetLinkRequest request)
    {
        if (await GetEligibleUserAsync(userId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.ExternalStudentId))
            return BadRequest("Choose a student.");
        var student = await StudentLookup.FindByExternalStudentIdAsync(
            request.ExternalStudentId.Trim(), HttpContext.RequestAborted);
        if (student is null) return BadRequest("The selected student is no longer available in Elements.");

        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var profile = await profiles.FindAsync(x => x.Provider == student.Provider &&
            x.ExternalStudentId == student.ExternalStudentId);
        if (profile is null)
            profile = await profiles.InsertAsync(new StudentProfile(guidGenerator.Create(), currentTenant.Id,
                null, student), autoSave: true);
        else
        {
            profile.Update(student);
            await profiles.UpdateAsync(profile, autoSave: true);
        }

        var link = await links.FindAsync(x => x.StaffUserId == userId);
        if (link is null)
            await links.InsertAsync(new StaffStudentLink(guidGenerator.Create(), currentTenant.Id,
                userId, profile.Id), autoSave: true);
        else
        {
            link.ChangeStudent(profile.Id);
            await links.UpdateAsync(link, autoSave: true);
        }
        await uow.CompleteAsync();
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid userId)
    {
        if (await GetEligibleUserAsync(userId) is null) return NotFound();
        var link = await links.FindAsync(x => x.StaffUserId == userId);
        if (link is not null) await links.DeleteAsync(link, autoSave: true);
        return NoContent();
    }

    private async Task<IdentityUser?> GetEligibleUserAsync(Guid userId)
    {
        if (!string.Equals(currentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase))
            return null;
        var user = await users.FindAsync(userId);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) ||
            StudentOnlyAccountAccess.IsNelsonStudentMailbox(user.Email))
            return null;
        return await facultyReader.HasActiveStaffIdentityAsync(user.Email, HttpContext.RequestAborted)
            ? user
            : null;
    }

    public sealed record SetLinkRequest(string ExternalStudentId);

    private IStudentInformationSystemStudentLookup StudentLookup => studentLookups.Single(x =>
        x.Provider == StudentInformationSystemProvider.ThesisElements);
}
