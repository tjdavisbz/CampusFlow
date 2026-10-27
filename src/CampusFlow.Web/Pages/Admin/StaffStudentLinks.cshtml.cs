using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Identity;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.AccessManagement)]
public class StaffStudentLinksModel(
    IRepository<IdentityUser, Guid> users,
    IRepository<StudentProfile, Guid> profiles,
    IRepository<StaffStudentLink, Guid> links,
    ICamsStaffEmailLookup staffEmails,
    FacultyPortalReader facultyReader,
    IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
    IGuidGenerator guidGenerator,
    IUnitOfWorkManager unitOfWorkManager,
    ITenantThemeProvider themeProvider) : CampusFlowPageModel
{
    [BindProperty] public Guid StaffUserId { get; set; }
    [BindProperty] public string ExternalStudentId { get; set; } = string.Empty;
    public string Search { get; private set; } = string.Empty;
    public string StudentSearch { get; private set; } = string.Empty;
    public IReadOnlyList<StaffAccount> StaffResults { get; private set; } = [];
    public StaffAccount? SelectedStaff { get; private set; }
    public StudentProfile? LinkedStudent { get; private set; }
    public IReadOnlyList<StudentInformationSystemStudent> StudentResults { get; private set; } = [];
    public TenantTheme Theme { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string? search, Guid? userId, string? studentSearch)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        Theme = themeProvider.Get(CurrentTenant.Name);
        Search = search?.Trim() ?? string.Empty;
        StudentSearch = studentSearch?.Trim() ?? string.Empty;

        if (Search.Length >= 2)
        {
            var allowed = (await staffEmails.GetNormalizedEmailsAsync(HttpContext.RequestAborted)).ToArray();
            StaffResults = (await StaffUserDirectoryQuery.ApplySort(
                    StaffUserDirectoryQuery.ApplyFilter(await users.GetQueryableAsync(), allowed, Search),
                    "name").Take(25).ToListAsync(HttpContext.RequestAborted))
                .Select(x => new StaffAccount(x.Id, x.Email, x.Name, x.Surname)).ToArray();
        }

        if (userId.HasValue)
        {
            var user = await users.FindAsync(userId.Value);
            if (user is null || !await IsEligibleStaffAsync(user)) return NotFound();
            SelectedStaff = new StaffAccount(user.Id, user.Email, user.Name, user.Surname);
            StaffUserId = user.Id;
            var link = await links.FindAsync(x => x.StaffUserId == user.Id);
            if (link is not null) LinkedStudent = await profiles.FindAsync(link.StudentProfileId);
            if (StudentSearch.Length >= 2)
                StudentResults = await StudentLookup.SearchAsync(StudentSearch, cancellationToken: HttpContext.RequestAborted);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostLinkAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        var user = await users.FindAsync(StaffUserId);
        if (user is null || !await IsEligibleStaffAsync(user)) return Forbid();
        var student = await StudentLookup.FindByExternalStudentIdAsync(ExternalStudentId, HttpContext.RequestAborted);
        if (student is null) return BadRequest("The selected student is no longer available in Elements.");

        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var profile = await profiles.FindAsync(x => x.Provider == student.Provider &&
            x.ExternalStudentId == student.ExternalStudentId);
        if (profile is null)
            profile = await profiles.InsertAsync(new StudentProfile(guidGenerator.Create(), CurrentTenant.Id,
                null, student), autoSave: true);
        else
        {
            profile.Update(student);
            await profiles.UpdateAsync(profile, autoSave: true);
        }

        var link = await links.FindAsync(x => x.StaffUserId == user.Id);
        if (link is null)
            await links.InsertAsync(new StaffStudentLink(guidGenerator.Create(), CurrentTenant.Id,
                user.Id, profile.Id), autoSave: true);
        else
        {
            link.ChangeStudent(profile.Id);
            await links.UpdateAsync(link, autoSave: true);
        }
        await uow.CompleteAsync();
        Alerts.Success($"{user.Email} is now linked to student {student.StudentId}.");
        return RedirectToPage(new { userId = user.Id });
    }

    public async Task<IActionResult> OnPostUnlinkAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        var link = await links.FindAsync(x => x.StaffUserId == StaffUserId);
        if (link is not null) await links.DeleteAsync(link, autoSave: true);
        Alerts.Success("Staff-to-student link removed. The student record was not deleted.");
        return RedirectToPage(new { userId = StaffUserId });
    }

    private async Task<bool> IsEligibleStaffAsync(IdentityUser user) =>
        !string.IsNullOrWhiteSpace(user.Email) &&
        !StudentOnlyAccountAccess.IsNelsonStudentMailbox(user.Email) &&
        await facultyReader.HasActiveStaffIdentityAsync(user.Email, HttpContext.RequestAborted);

    private IStudentInformationSystemStudentLookup StudentLookup => studentLookups.Single(x =>
        x.Provider == StudentInformationSystemProvider.ThesisElements);

    public sealed record StaffAccount(Guid Id, string Email, string? FirstName, string? LastName)
    {
        public string DisplayName => $"{FirstName} {LastName}".Trim();
    }
}
