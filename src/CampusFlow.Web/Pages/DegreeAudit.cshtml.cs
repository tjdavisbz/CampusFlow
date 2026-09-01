using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
public class DegreeAuditModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemDegreeAuditLookup> _lookups;
    private readonly ILogger<DegreeAuditModel> _logger;

    public DegreeAuditModel(
        ITenantThemeProvider themeProvider,
        IRepository<StudentProfile, Guid> profiles,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemDegreeAuditLookup> lookups,
        ILogger<DegreeAuditModel> logger)
    {
        _themeProvider = themeProvider;
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _lookups = lookups.ToArray();
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)] public int? RevisionTermId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AuditDegreeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AuditProgramId { get; set; }
    [BindProperty(SupportsGet = true)] public bool RefreshComplete { get; set; }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentDisplayName { get; private set; } = "Student";
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public bool IsUnavailable { get; private set; }
    public IReadOnlyList<StudentDegreeAuditSummary> Audits { get; private set; } = [];
    public StudentDegreeAuditDetail? SelectedAudit { get; private set; }
    public IReadOnlyList<RequirementGroup> Requirements { get; private set; } = [];
    public bool IsReadOnlyStudentView => _currentStudentView.IsImpersonating;

    public async Task<IActionResult> OnPostRefreshAsync(
        int revisionTermId,
        int auditDegreeId,
        int auditProgramId)
    {
        if (CurrentUser.Id is null)
        {
            return Unauthorized();
        }

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        var lookup = profile is null
            ? null
            : _lookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (profile is null || lookup is null)
        {
            return new JsonResult(new { refreshed = false }) { StatusCode = 404 };
        }

        try
        {
            var audits = await lookup.GetAuditsAsync(
                profile.ExternalStudentId, HttpContext.RequestAborted);
            if (!audits.Any(x => x.RevisionTermId == revisionTermId &&
                                 x.AuditDegreeId == auditDegreeId &&
                                 x.AuditProgramId == auditProgramId))
            {
                return new JsonResult(new { refreshed = false }) { StatusCode = 404 };
            }

            await lookup.RefreshAuditAsync(
                profile.ExternalStudentId, revisionTermId, auditDegreeId, auditProgramId,
                HttpContext.RequestAborted);
            return new JsonResult(new { refreshed = true });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Unable to refresh the current student's degree audit in Elements.");
            return new JsonResult(new { refreshed = false }) { StatusCode = 502 };
        }
    }

    public async Task OnGetAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        if (CurrentUser.Id is null) return;

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null)
        {
            IsUnavailable = true;
            return;
        }

        StudentDisplayName = profile.DisplayName;
        StudentIdentifier = profile.StudentId;
        var lookup = _lookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (lookup is null)
        {
            IsUnavailable = true;
            return;
        }

        try
        {
            Audits = await lookup.GetAuditsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            var selected = Audits.FirstOrDefault(x =>
                               x.RevisionTermId == RevisionTermId && x.AuditDegreeId == AuditDegreeId &&
                               x.AuditProgramId == AuditProgramId)
                           ?? Audits.FirstOrDefault();
            if (selected is null) return;

            RevisionTermId = selected.RevisionTermId;
            AuditDegreeId = selected.AuditDegreeId;
            AuditProgramId = selected.AuditProgramId;
            SelectedAudit = await lookup.GetAuditAsync(
                profile.ExternalStudentId, selected.RevisionTermId, selected.AuditDegreeId,
                selected.AuditProgramId, HttpContext.RequestAborted);
            Requirements = BuildRequirements(SelectedAudit?.Courses ?? []);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsUnavailable = true;
            _logger.LogWarning(exception, "Unable to load the degree audit for the current student.");
        }
    }

    public decimal CompletionPercent => SelectedAudit?.Summary.CreditsRequired > 0
        ? Math.Min(100m, SelectedAudit.Summary.CreditsCompleted / SelectedAudit.Summary.CreditsRequired * 100m)
        : 0m;

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string StatusClass(string status) => status.Trim().ToUpperInvariant() switch
    {
        "C" or "MC" or "TC" or "COMPLETED" => "complete",
        "INP" or "IN PROGRESS" => "progress",
        "NN" or "NOTNECESSARY" => "neutral",
        "E" or "ELECTIVE" => "extra",
        _ => "remaining"
    };

    public static string StatusLabel(string status) => status.Trim().ToUpperInvariant() switch
    {
        "C" => "Completed", "MC" => "Manual map completed", "TC" => "Transfer completed",
        "MR" => "Manual map remaining", "TR" => "Transfer remaining", "INP" => "In progress",
        "NN" or "NOTNECESSARY" => "Not necessary", "E" => "Extra course", "R" => "Remaining",
        _ => string.IsNullOrWhiteSpace(status) ? "Remaining" : status
    };

    private static IReadOnlyList<RequirementGroup> BuildRequirements(IReadOnlyList<StudentDegreeAuditCourse> courses) =>
        courses.GroupBy(x => new
            {
                x.RequirementName, x.RequirementSortOrder, x.RequirementStatus,
                x.RequirementCreditsRequired, x.RequirementCreditsCompleted,
                x.RequirementMinimumGpa, x.RequirementGpaAttained
            })
            .OrderBy(x => x.Key.RequirementSortOrder)
            .Select(requirement => new RequirementGroup(
                requirement.Key.RequirementName, requirement.Key.RequirementStatus,
                requirement.Key.RequirementCreditsRequired, requirement.Key.RequirementCreditsCompleted,
                requirement.Key.RequirementMinimumGpa, requirement.Key.RequirementGpaAttained,
                requirement.GroupBy(x => new
                    {
                        x.GroupName, x.GroupSortOrder, x.GroupStatus, x.GroupCreditsRequired,
                        x.GroupCreditsCompleted, x.GroupMinimumGpa, x.GroupGpaAttained
                    })
                    .OrderBy(x => x.Key.GroupSortOrder)
                    .Select(group => new CourseGroup(
                        group.Key.GroupName, group.Key.GroupStatus, group.Key.GroupCreditsRequired,
                        group.Key.GroupCreditsCompleted, group.Key.GroupMinimumGpa,
                        group.Key.GroupGpaAttained, group.ToArray()))
                    .ToArray()))
            .ToArray();

    public sealed record RequirementGroup(string Name, string Status, decimal CreditsRequired,
        decimal CreditsCompleted, decimal MinimumGpa, decimal GpaAttained, IReadOnlyList<CourseGroup> Groups);
    public sealed record CourseGroup(string Name, string Status, decimal CreditsRequired,
        decimal CreditsCompleted, decimal MinimumGpa, decimal GpaAttained,
        IReadOnlyList<StudentDegreeAuditCourse> Courses);
}
