using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Announcements;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Announcements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.GlobalConfiguration)]
public class AnnouncementsModel : CampusFlowPageModel
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly IRepository<PortalAnnouncement, Guid> _announcements;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _configuredTerms;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ITenantThemeProvider _themes;
    private readonly IGuidGenerator _guids;
    private readonly ILogger<AnnouncementsModel> _logger;

    public AnnouncementsModel(IRepository<PortalAnnouncement, Guid> announcements,
        IRepository<RegistrationTermConfiguration, Guid> configuredTerms,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        ITenantThemeProvider themes, IGuidGenerator guids, ILogger<AnnouncementsModel> logger)
    {
        _announcements = announcements;
        _configuredTerms = configuredTerms;
        _termLookups = termLookups.ToArray();
        _themes = themes;
        _guids = guids;
        _logger = logger;
    }

    [BindProperty] public AnnouncementInput Input { get; set; } = new();
    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<PortalAnnouncement> Items { get; private set; } = [];
    public IReadOnlyList<string> TermNames { get; private set; } = [];

    public async Task OnGetAsync(Guid? edit = null)
    {
        await LoadAsync();
        if (edit is not null)
        {
            var item = Items.FirstOrDefault(x => x.Id == edit.Value);
            if (item is not null)
                Input = new AnnouncementInput
                {
                    Id = item.Id,
                    Title = item.Title,
                    ContentHtml = item.ContentHtml,
                    StartsAtLocal = ToCentral(item.StartsAtUtc),
                    EndsAtLocal = ToCentral(item.EndsAtUtc),
                    ForStudents = item.Audience.HasFlag(AnnouncementAudience.Student),
                    ForFaculty = item.Audience.HasFlag(AnnouncementAudience.Faculty),
                    AllTerms = item.Terms.Count == 0,
                    Terms = item.Terms.ToList(),
                    IsPublished = item.IsPublished
                };
        }
        else
        {
            Input.StartsAtLocal = ToCentral(DateTime.UtcNow);
            Input.EndsAtLocal = Input.StartsAtLocal.AddMonths(3);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAsync();
        var safeHtml = AnnouncementHtml.Sanitize(Input.ContentHtml);
        if (string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(safeHtml, "<[^>]*>", "")))
            ModelState.AddModelError("Input.ContentHtml", "Enter announcement content.");
        if (!Input.ForStudents && !Input.ForFaculty)
            ModelState.AddModelError("Input.ForStudents", "Choose at least one audience.");
        if (!Input.AllTerms && Input.Terms.Count == 0)
            ModelState.AddModelError("Input.Terms", "Select at least one term or choose all terms.");
        if (Input.Terms.Except(TermNames, StringComparer.OrdinalIgnoreCase).Any())
            ModelState.AddModelError("Input.Terms", "Choose terms from the current term list.");
        if (Input.EndsAtLocal <= Input.StartsAtLocal)
            ModelState.AddModelError("Input.EndsAtLocal", "The end must be after the start.");
        if (!ModelState.IsValid) return Page();

        var audience = (Input.ForStudents ? AnnouncementAudience.Student : 0) |
                       (Input.ForFaculty ? AnnouncementAudience.Faculty : 0);
        var starts = ToUtc(Input.StartsAtLocal);
        var ends = ToUtc(Input.EndsAtLocal);
        var terms = Input.AllTerms ? [] : Input.Terms;
        if (Input.Id.HasValue)
        {
            var existing = Items.FirstOrDefault(x => x.Id == Input.Id.Value);
            if (existing is null) return NotFound();
            existing.Update(Input.Title, safeHtml, starts, ends, audience, terms, Input.IsPublished);
            await _announcements.UpdateAsync(existing, autoSave: true);
        }
        else
        {
            await _announcements.InsertAsync(new PortalAnnouncement(_guids.Create(), CurrentTenant.Id,
                Input.Title, safeHtml, starts, ends, audience, terms, Input.IsPublished), autoSave: true);
        }
        Alerts.Success("Announcement saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var item = await _announcements.FindAsync(id);
        if (item is null) return NotFound();
        await _announcements.DeleteAsync(item, autoSave: true);
        Alerts.Success("Announcement deleted.");
        return RedirectToPage();
    }

    public static DateTime ToCentral(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Central);

    private static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Central);

    private async Task LoadAsync()
    {
        Theme = _themes.Get(CurrentTenant.Name);
        Items = (await _announcements.GetListAsync()).OrderByDescending(x => x.StartsAtUtc).ToArray();
        var lookup = _termLookups.FirstOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        try
        {
            if (lookup is not null)
                TermNames = (await lookup.GetTermsAsync(HttpContext.RequestAborted))
                    .Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Unable to load announcement terms from Elements.");
        }
        TermNames = TermNames.Concat((await _configuredTerms.GetListAsync()).Select(x => x.TermName))
            .Concat(Items.SelectMany(x => x.Terms))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x).ToArray();
    }

    public class AnnouncementInput
    {
        public Guid? Id { get; set; }
        [Required, StringLength(160)] public string Title { get; set; } = string.Empty;
        [Required, StringLength(20000)] public string ContentHtml { get; set; } = string.Empty;
        [Required] public DateTime StartsAtLocal { get; set; }
        [Required] public DateTime EndsAtLocal { get; set; }
        public bool ForStudents { get; set; } = true;
        public bool ForFaculty { get; set; }
        public bool AllTerms { get; set; } = true;
        public List<string> Terms { get; set; } = [];
        public bool IsPublished { get; set; } = true;
    }
}
