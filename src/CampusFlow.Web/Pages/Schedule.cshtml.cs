using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
public class ScheduleModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemScheduleLookup> _scheduleLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ILogger<ScheduleModel> _logger;

    public ScheduleModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemScheduleLookup> scheduleLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        ILogger<ScheduleModel> logger)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _currentStudentView = currentStudentView;
        _scheduleLookups = scheduleLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _logger = logger;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentDisplayName { get; private set; } = "Student";
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public string? CurrentTermCode { get; private set; }
    public bool IsScheduleUnavailable { get; private set; }
    public IReadOnlyList<ScheduleTermGroup> CurrentAndUpcomingTerms { get; private set; } = [];
    public IReadOnlyList<ScheduleTermGroup> HistoricalTerms { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        if (CurrentUser.Id is null)
        {
            return;
        }

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null)
        {
            IsScheduleUnavailable = true;
            return;
        }

        StudentDisplayName = profile.DisplayName;
        StudentIdentifier = profile.StudentId;
        var scheduleLookup = _scheduleLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (scheduleLookup is null)
        {
            IsScheduleUnavailable = true;
            return;
        }

        try
        {
            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var currentTerm = termLookup is null
                ? null
                : await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            CurrentTermCode = currentTerm?.TermCode;

            var courses = await scheduleLookup.GetScheduleAsync(
                profile.ExternalStudentId,
                HttpContext.RequestAborted);
            var terms = courses
                .GroupBy(x => new { x.TermCode, x.TermName, x.ExternalTermId })
                .OrderByDescending(x => x.Key.TermCode)
                .Select(group => new ScheduleTermGroup(
                    group.Key.TermCode,
                    group.Key.TermName,
                    group.Key.ExternalTermId,
                    group.Sum(x => x.Credits),
                    group.OrderBy(x => x.Department).ThenBy(x => x.CourseNumber).ToArray()))
                .ToArray();

            CurrentAndUpcomingTerms = currentTerm is null
                ? []
                : terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) >= 0).ToArray();
            HistoricalTerms = currentTerm is null
                ? terms
                : terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) < 0).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsScheduleUnavailable = true;
            _logger.LogWarning(exception, "Unable to load the schedule for the current student.");
        }
    }

    public static string FormatCredits(decimal credits) =>
        credits.ToString("0.##", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeSpan? time) =>
        time is null ? string.Empty : DateTime.Today.Add(time.Value).ToString("h:mm tt");

    public sealed record ScheduleTermGroup(
        string TermCode,
        string TermName,
        string ExternalTermId,
        decimal TotalCredits,
        IReadOnlyList<StudentCourseScheduleItem> Courses);
}
