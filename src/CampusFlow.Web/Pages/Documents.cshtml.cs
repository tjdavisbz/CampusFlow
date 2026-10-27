using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
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
public class DocumentsModel(
    ITenantThemeProvider themeProvider,
    ICurrentStudentView currentStudentView,
    StudentDocumentReader documentReader,
    ILogger<DocumentsModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public bool ShowAll { get; set; }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public IReadOnlyList<StudentTrackedDocument> Documents { get; private set; } = [];
    public bool IsUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        NoStore();
        Theme = themeProvider.Get(CurrentTenant.Name);
        var studentUid = await GetStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            Documents = await documentReader.GetDocumentsAsync(studentUid.Value, ShowAll,
                HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load document tracking for the current student.");
            IsUnavailable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnGetImageAsync(int imageId)
    {
        NoStore();
        var studentUid = await GetStudentUidAsync();
        if (!studentUid.HasValue) return Forbid();
        try
        {
            await using var image = await documentReader.OpenVisibleImageAsync(studentUid.Value,
                imageId, HttpContext.RequestAborted);
            if (image is null) return NotFound();

            var inline = IsSafeInlineType(image.ContentType);
            Response.ContentType = inline ? image.ContentType : "application/octet-stream";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-Frame-Options"] = "DENY";
            var fileName = Path.GetFileName(image.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "document";
            Response.Headers.ContentDisposition = new ContentDispositionHeaderValue(
                inline ? "inline" : "attachment") { FileNameStar = fileName }.ToString();
            await image.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested && !Response.HasStarted)
        {
            logger.LogWarning(exception, "Unable to open a student-visible document image.");
            return StatusCode(502);
        }
    }

    public static bool IsSafeInlineType(string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    private async Task<int?> GetStudentUidAsync()
    {
        if (CurrentUser.Id is null ||
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
