using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Chapel;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Admin.Chapel;

public sealed record ChapelSessionSummary(DateTime ChapelAtLocal, int Total, int Present,
    int Absent, int Excused, int ScanInOnly);

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AttendanceModel(
    ChapelAttendanceAppService attendance,
    IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    ITenantThemeProvider themeProvider,
    ILogger<AttendanceModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Student { get; set; }
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dorm { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowScanInOnly { get; set; }
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<string> Groups { get; private set; } = [];
    public IReadOnlyList<ChapelAttendanceRow> Rows { get; private set; } = [];
    public IReadOnlyList<ChapelSessionSummary> SessionSummaries { get; private set; } = [];
    public bool IsTruncated { get; private set; }
    public int ResultLimit => Date.HasValue ? ChapelAttendanceAppService.DaySearchLimit : ChapelAttendanceAppService.GeneralSearchLimit;
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public bool IsUnavailable { get; private set; }
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        Theme = themeProvider.Get(CurrentTenant.Name);
        try
        {
            var terms = termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            Terms = await terms.GetTermsAsync(HttpContext.RequestAborted);
            if (!TermId.HasValue)
            {
                var current = await terms.GetCurrentTermAsync(HttpContext.RequestAborted);
                if (current is not null) TermId = int.Parse(current.ExternalTermId);
            }
            if (TermId.HasValue) Groups = await attendance.GetGroupNamesAsync(TermId.Value);
            if (TermId.HasValue && (Date.HasValue || !string.IsNullOrWhiteSpace(Student) || !string.IsNullOrWhiteSpace(Group) || !string.IsNullOrWhiteSpace(Dorm)))
            {
                var found = await attendance.SearchAsync(TermId.Value, Date, Student, Group, Dorm);
                IsTruncated = found.Count > ResultLimit;
                var visible = found.Take(ResultLimit).ToList();
                if (Date.HasValue && string.IsNullOrWhiteSpace(Student) && string.IsNullOrWhiteSpace(Group) && string.IsNullOrWhiteSpace(Dorm) && !IsTruncated)
                    SessionSummaries = visible.GroupBy(x => x.ChapelAtLocal).OrderBy(x => x.Key)
                        .Select(x => new ChapelSessionSummary(x.Key, x.Count(),
                            x.Count(r => r.Status == ChapelAttendanceStatus.Present),
                            x.Count(r => r.Status == ChapelAttendanceStatus.Absent),
                            x.Count(r => r.Status == ChapelAttendanceStatus.Excused),
                            x.Count(r => r.IsScanInOnly))).ToList();
                Rows = ShowScanInOnly && Date.HasValue ? visible.Where(x => x.IsScanInOnly).ToList() : visible;
            }
            Message = TempData["ChapelMessage"] as string;
            ErrorMessage = TempData["ChapelError"] as string;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load the chapel attendance workspace.");
            IsUnavailable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid id, string revision,
        ChapelAttendanceStatus status, string? comment, int termId, DateTime? date, string? student,
        string? group, bool showScanInOnly, string? dorm)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        await attendance.UpdateAsync(new ChapelAttendanceUpdate(id, revision, status, comment));
        TempData["ChapelMessage"] = "Attendance saved.";
        return RedirectToPage(new { termId, date = date?.ToString("yyyy-MM-dd"), student, group, showScanInOnly, dorm });
    }

    public async Task<IActionResult> OnPostAutoSaveAsync(Guid id, string revision, ChapelAttendanceStatus? status, string? comment)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (!ModelState.IsValid || id == Guid.Empty || string.IsNullOrWhiteSpace(revision) || !status.HasValue || !Enum.IsDefined(status.Value) || comment?.Length > 4000)
            return new JsonResult(new { message = "Choose a valid status and a comment of at most 4,000 characters." }) { StatusCode = 400 };
        try { return new JsonResult(await attendance.UpdateAsync(new(id, revision, status!.Value, comment))); }
        catch (Volo.Abp.UserFriendlyException e) { return new JsonResult(new { message = e.Message }) { StatusCode = 409 }; }
        catch (Volo.Abp.Data.AbpDbConcurrencyException) { return new JsonResult(new { message = "This record changed elsewhere. Reload it before saving." }) { StatusCode = 409 }; }
    }
    public async Task<IActionResult> OnGetAttendanceRowAsync(Guid id)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        try { return new JsonResult(await attendance.GetRowAsync(id)); }
        catch (Volo.Abp.UserFriendlyException) { return NotFound(); }
    }
    public async Task<IActionResult> OnGetDaySummaryAsync(int termId, DateTime date)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (termId <= 0 || date == default) return BadRequest();
        var rows = await attendance.SearchAsync(termId, date, null, null);
        if (rows.Count > ChapelAttendanceAppService.DaySearchLimit) return BadRequest();
        return new JsonResult(rows.GroupBy(x => x.ChapelAtLocal).OrderBy(x => x.Key)
            .Select(x => new
            {
                session = x.Key.ToString("yyyy-MM-ddTHH:mm:ss"),
                total = x.Count(),
                present = x.Count(r => r.Status == ChapelAttendanceStatus.Present),
                absent = x.Count(r => r.Status == ChapelAttendanceStatus.Absent),
                excused = x.Count(r => r.Status == ChapelAttendanceStatus.Excused),
                scanInOnly = x.Count(r => r.IsScanInOnly),
                confirmation = $"Credit all {x.Count(r => r.IsScanInOnly)} scan-in-only absences for the {x.Key:h:mm tt} chapel as Present? Use this only if scan-out was waived for this session. This cannot be undone as a group."
            }));
    }

    public async Task<IActionResult> OnPostCreditScanInOnlyAsync(int termId, string sessionAtLocal,
        int expectedCount)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (!DateTime.TryParseExact(sessionAtLocal, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var chapelAtLocal)) return BadRequest();
        try
        {
            var credited = await attendance.CreditScanInOnlySessionAsync(termId, chapelAtLocal, expectedCount);
            TempData["ChapelMessage"] = $"{credited} scan-in-only attendance record(s) marked present for this chapel session.";
        }
        catch (Volo.Abp.UserFriendlyException exception)
        {
            TempData["ChapelError"] = exception.Message;
        }
        return RedirectToPage(new { termId, date = chapelAtLocal.ToString("yyyy-MM-dd") });
    }
}
