using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages;

[Authorize]
public class PrivacyPasswordsModel(
    ITenantThemeProvider tenantThemeProvider,
    ICurrentStudentView currentStudentView,
    IStudentFerpaService ferpaService,
    ILogger<PrivacyPasswordsModel> logger) : CampusFlowPageModel
{
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);

    public IReadOnlyList<StudentFerpaDisplay> Entries { get; private set; } = [];
    public bool IsUnavailable { get; private set; }
    public string? Feedback => TempData["PrivacyPasswordsFeedback"] as string;
    public string? Error => TempData["PrivacyPasswordsError"] as string;

    public async Task<IActionResult> OnGetAsync()
    {
        NoStore();
        Theme = tenantThemeProvider.Get(CurrentTenant.Name);
        var studentUid = await GetCurrentStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();

        try
        {
            Entries = (await ferpaService.GetAsync(studentUid.Value, HttpContext.RequestAborted))
                .Select(StudentFerpaFormatting.Describe).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load privacy-password entries for current student.");
            IsUnavailable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostRevealAsync(int id)
    {
        NoStore();
        var studentUid = await GetCurrentStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            var entry = (await ferpaService.GetAsync(studentUid.Value, HttpContext.RequestAborted))
                .SingleOrDefault(x => x.Id == id);
            if (entry is null) return NotFound();
            return new JsonResult(new { value = StudentFerpaFormatting.Reveal(entry) });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to reveal a privacy-password entry.");
            return StatusCode(502);
        }
    }

    public async Task<IActionResult> OnPostAddIndividualAsync(string password)
    {
        NoStore();
        var studentUid = await GetCurrentStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            await ferpaService.AddIndividualAsync(studentUid.Value, password, HttpContext.RequestAborted);
            TempData["PrivacyPasswordsFeedback"] = "Your individual privacy password was added.";
        }
        catch (ArgumentException exception)
        {
            TempData["PrivacyPasswordsError"] = exception.Message;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("An individual privacy password already exists", StringComparison.Ordinal))
        {
            TempData["PrivacyPasswordsError"] = exception.Message;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to add an individual privacy password.");
            TempData["PrivacyPasswordsError"] = "The password could not be saved. Please try again.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddContactAsync(string relationship, string firstName,
        string lastName, string password)
    {
        NoStore();
        var studentUid = await GetCurrentStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            await ferpaService.AddContactAsync(studentUid.Value, relationship, firstName,
                lastName, password, HttpContext.RequestAborted);
            TempData["PrivacyPasswordsFeedback"] = "Your authorized person was added.";
        }
        catch (ArgumentException exception)
        {
            TempData["PrivacyPasswordsError"] = exception.Message;
        }
        catch (InvalidOperationException exception) when (exception.Message == "That authorized-person entry already exists.")
        {
            TempData["PrivacyPasswordsError"] = exception.Message;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to add an authorized privacy-password contact.");
            TempData["PrivacyPasswordsError"] = "The authorized person could not be saved. Please try again.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        NoStore();
        var studentUid = await GetCurrentStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            await ferpaService.DeleteAsync(studentUid.Value, id, HttpContext.RequestAborted);
            TempData["PrivacyPasswordsFeedback"] = "The privacy-password entry was removed.";
        }
        catch (InvalidOperationException exception) when (exception.Message == "That privacy-password entry is not available for this student.")
        {
            return NotFound();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to remove a privacy-password entry.");
            TempData["PrivacyPasswordsError"] = "The entry could not be removed. Please try again.";
        }
        return RedirectToPage();
    }

    private async Task<int?> GetCurrentStudentUidAsync()
    {
        // An admin's read-only impersonation must never reveal or change FERPA passwords.
        if (currentStudentView.IsImpersonating || CurrentUser.Id is null ||
            !string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return null;
        var profile = await currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        return profile?.Provider == StudentInformationSystemProvider.ThesisElements &&
               int.TryParse(profile.ExternalStudentId, out var uid) && uid > 0 ? uid : null;
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
