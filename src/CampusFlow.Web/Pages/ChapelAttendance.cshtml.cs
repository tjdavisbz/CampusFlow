using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Chapel;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ChapelAttendanceModel(
    ITenantThemeProvider themeProvider,
    ICurrentStudentView currentStudentView,
    IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    IRepository<ChapelAttendanceRecord, Guid> records,
    ILogger<ChapelAttendanceModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<ChapelAttendanceRecord> Attendance { get; private set; } = [];
    public int PresentCount => Attendance.Count(x => x.Status == ChapelAttendanceStatus.Present);
    public int AbsentCount => Attendance.Count(x => x.Status == ChapelAttendanceStatus.Absent);
    public int ExcusedCount => Attendance.Count(x => x.Status == ChapelAttendanceStatus.Excused);
    public bool IsUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        Theme = themeProvider.Get(CurrentTenant.Name);
        var profile = await currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile?.Provider != StudentInformationSystemProvider.ThesisElements ||
            !int.TryParse(profile.ExternalStudentId, out var studentUid) || studentUid <= 0) return Forbid();

        try
        {
            var terms = termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            Terms = await terms.GetTermsAsync(HttpContext.RequestAborted);
            var selected = TermId.HasValue
                ? Terms.FirstOrDefault(x => x.ExternalTermId == TermId.Value.ToString())
                : await terms.GetCurrentTermAsync(HttpContext.RequestAborted);
            if (selected is null) { TermId = null; return Page(); }
            TermId = int.Parse(selected.ExternalTermId);
            var tenant = CurrentTenant.Id;
            var campusTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, campusTimeZone);
            var query = (await records.GetQueryableAsync())
                .Where(x => x.TenantId == tenant && x.StudentUid == studentUid &&
                            x.TermCalendarId == TermId.Value && x.ChapelAtLocal <= now)
                .OrderBy(x => x.ChapelAtLocal);
            Attendance = await query.ToListAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load chapel attendance for the signed-in student.");
            IsUnavailable = true;
        }
        return Page();
    }
}
