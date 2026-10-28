using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Chapel;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Admin.Chapel;

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExcuseRequestsModel(
    IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    IRepository<ChapelExcuseRequest, Guid> requests,
    ChapelExcuseReviewAppService review,
    ILogger<ExcuseRequestsModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Student { get; set; }
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<ChapelExcuseRequest> Rows { get; private set; } = [];
    public bool IsUnavailable { get; private set; }
    public string? Feedback { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        Feedback = TempData["ChapelReviewMessage"] as string;
        try
        {
            var lookup = termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            Terms = await lookup.GetTermsAsync(HttpContext.RequestAborted);
            var selected = TermId.HasValue
                ? Terms.FirstOrDefault(x => x.ExternalTermId == TermId.Value.ToString())
                : await lookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            if (selected is null) { TermId = null; return Page(); }
            TermId = int.Parse(selected.ExternalTermId);
            var query = (await requests.GetQueryableAsync()).Where(x =>
                x.TenantId == CurrentTenant.Id && x.TermCalendarId == TermId.Value);
            if (!string.IsNullOrWhiteSpace(Student))
            {
                var text = Student.Trim();
                query = query.Where(x => x.StudentId.Contains(text) || x.StudentName.Contains(text));
            }
            Rows = await query.OrderByDescending(x => x.CreationTime).Take(300)
                .ToListAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load chapel excuse requests.");
            IsUnavailable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid requestId, string revision,
        int? termId, string? student)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return NotFound();
        var result = await review.ApproveAsync(requestId, revision);
        TempData["ChapelReviewMessage"] = result.NewlyExcusedSessions == 0
            ? "Request approved. Matching sessions were already marked Present or Excused; no attendance was changed."
            : $"Request approved. {result.NewlyExcusedSessions} chapel attendance record(s) marked Excused.";
        return RedirectToPage(new { termId, student });
    }
}
