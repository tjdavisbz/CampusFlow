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
public class ChapelExcuseRequestsModel(
    ITenantThemeProvider themeProvider,
    ICurrentStudentView currentStudentView,
    IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    IRepository<ChapelExcuseRequest, Guid> requests,
    ILogger<ChapelExcuseRequestsModel> logger) : CampusFlowPageModel
{
    [BindProperty(SupportsGet = true)] public int? TermId { get; set; }
    [BindProperty] public DateTime? StartDate { get; set; }
    [BindProperty] public DateTime? EndDate { get; set; }
    [BindProperty] public string? ExcuseType { get; set; }
    [BindProperty] public string? Reason { get; set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<ChapelExcuseRequest> PreviousRequests { get; private set; } = [];
    public string? Feedback => TempData["ChapelExcuseFeedback"] as string;
    public string? Error { get; private set; }
    public bool IsUnavailable { get; private set; }
    public bool IsImpersonating => currentStudentView.IsImpersonating;

    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var profile = await GetProfileAsync();
        if (profile is null) return Forbid();
        Theme = themeProvider.Get(CurrentTenant.Name);
        await LoadAsync(profile);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int termId)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var profile = await GetProfileAsync();
        if (profile is null) return Forbid();
        if (currentStudentView.IsImpersonating) return Forbid();
        Theme = themeProvider.Get(CurrentTenant.Name);
        TermId = termId;
        try
        {
            var lookup = termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            var terms = await lookup.GetTermsAsync(HttpContext.RequestAborted);
            var term = terms.SingleOrDefault(x => x.ExternalTermId == termId.ToString());
            if (term is null) throw new ArgumentException("Choose a valid term.");
            if (!StartDate.HasValue || !EndDate.HasValue)
                throw new ArgumentException("Enter a start and end date.");
            var start = DateOnly.FromDateTime(StartDate.Value);
            var end = DateOnly.FromDateTime(EndDate.Value);
            if (start < DateOnly.FromDateTime(term.StartDate) ||
                end > DateOnly.FromDateTime(term.EndDate))
                throw new ArgumentException("The requested dates must be within the selected term.");
            var studentUid = int.Parse(profile.ExternalStudentId);
            var request = new ChapelExcuseRequest(Guid.NewGuid(), CurrentTenant.Id, termId,
                studentUid, profile.StudentId, profile.DisplayName, start, end,
                ExcuseType ?? "", Reason ?? "");
            var existing = (await requests.GetQueryableAsync()).Where(x =>
                x.TenantId == CurrentTenant.Id && x.TermCalendarId == termId &&
                x.StudentUid == studentUid && x.StartDate == start && x.EndDate == end &&
                x.ExcuseType == request.ExcuseType && x.Reason == request.Reason);
            if (await existing.AnyAsync(HttpContext.RequestAborted))
                throw new ArgumentException("This request has already been submitted.");
            await requests.InsertAsync(request, autoSave: true);
            TempData["ChapelExcuseFeedback"] = "Your request was submitted. Your attendance will not change unless chapel staff review and apply an excuse.";
            return RedirectToPage(new { termId });
        }
        catch (ArgumentException exception)
        {
            Error = exception.Message;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to submit chapel excuse request for current student.");
            Error = "Your request could not be submitted. Please try again.";
        }
        await LoadAsync(profile);
        return Page();
    }

    private async Task<StudentProfile?> GetProfileAsync()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return null;
        var profile = await currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        return profile?.Provider == StudentInformationSystemProvider.ThesisElements &&
               int.TryParse(profile.ExternalStudentId, out var uid) && uid > 0 ? profile : null;
    }

    private async Task LoadAsync(StudentProfile profile)
    {
        try
        {
            var lookup = termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
            Terms = await lookup.GetTermsAsync(HttpContext.RequestAborted);
            var selected = TermId.HasValue
                ? Terms.FirstOrDefault(x => x.ExternalTermId == TermId.Value.ToString())
                : await lookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            if (selected is null) { TermId = null; return; }
            TermId = int.Parse(selected.ExternalTermId);
            var studentUid = int.Parse(profile.ExternalStudentId);
            PreviousRequests = await (await requests.GetQueryableAsync())
                .Where(x => x.TenantId == CurrentTenant.Id && x.StudentUid == studentUid &&
                            x.TermCalendarId == TermId.Value)
                .OrderByDescending(x => x.CreationTime)
                .Take(100)
                .ToListAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load chapel excuse requests for current student.");
            IsUnavailable = true;
        }
    }
}
