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
using Volo.Abp;
namespace CampusFlow.Web.Pages.Admin.Chapel;

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DormsModel(ChapelAttendanceAppService attendance, IEnumerable<IStudentInformationSystemTermLookup> terms, ILogger<DormsModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dorm { get; set; }
    [BindProperty(SupportsGet = true)] public string? Student { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public ChapelAttendanceAppService.DormRosterPage? Roster { get; private set; }
    public string? Error { get; private set; }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        try
        {
            var lookup = terms.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            Terms = await lookup.GetTermsAsync(HttpContext.RequestAborted);
            TermId ??= int.TryParse((await lookup.GetCurrentTermAsync(HttpContext.RequestAborted))?.ExternalTermId, out var id) ? id : null;
            if (TermId.HasValue && !Terms.Any(x => x.ExternalTermId == TermId.Value.ToString())) return BadRequest();
            if (TermId.HasValue && !string.IsNullOrWhiteSpace(Dorm))
                Roster = await attendance.GetDormRosterAsync(TermId.Value, Dorm, PageNumber, Student);
        }
        catch (UserFriendlyException exception) { Error = exception.Message; }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load the chapel dorm roster.");
            Error = "The dorm roster is temporarily unavailable. Please try again later.";
        }
        return Page();
    }
}
