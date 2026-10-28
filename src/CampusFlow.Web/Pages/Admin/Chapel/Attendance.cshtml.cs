using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Chapel;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Admin.Chapel;

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AttendanceModel(
    ChapelAttendanceAppService attendance,
    IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    ILogger<AttendanceModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Student { get; set; }
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<string> Groups { get; private set; } = [];
    public IReadOnlyList<ChapelAttendanceRow> Rows { get; private set; } = [];
    public bool IsUnavailable { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
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
            if (TermId.HasValue && (Date.HasValue || !string.IsNullOrWhiteSpace(Student) || !string.IsNullOrWhiteSpace(Group)))
                Rows = await attendance.SearchAsync(TermId.Value, Date, Student, Group);
            Message = TempData["ChapelMessage"] as string;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load the chapel attendance workspace.");
            IsUnavailable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid id, string revision,
        ChapelAttendanceStatus status, string? comment, int termId, DateTime? date, string? student, string? group)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        await attendance.UpdateAsync(new ChapelAttendanceUpdate(id, revision, status, comment));
        TempData["ChapelMessage"] = "Attendance saved.";
        return RedirectToPage(new { termId, date = date?.ToString("yyyy-MM-dd"), student, group });
    }
}
