using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
public class CourseSelectionModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemDegreeAuditLookup> _degreeAuditLookups;
    private readonly ICourseSelectionAppService _courseSelection;
    private readonly ILogger<CourseSelectionModel> _logger;

    public CourseSelectionModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> profiles,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IEnumerable<IStudentInformationSystemDegreeAuditLookup> degreeAuditLookups,
        ICourseSelectionAppService courseSelection,
        ILogger<CourseSelectionModel> logger)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _profiles = profiles;
        _termLookups = termLookups.ToArray();
        _degreeAuditLookups = degreeAuditLookups.ToArray();
        _courseSelection = courseSelection;
        _logger = logger;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentDisplayName { get; private set; } = "Student";
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public CourseSelectionDto? Selection { get; private set; }
    public IReadOnlyList<CourseSelectionTermDto> EligibleTerms { get; private set; } = [];
    public bool IsUnavailable { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string ErrorTitle { get; private set; } = "Course not added";

    [BindProperty(SupportsGet = true)]
    public bool RefreshComplete { get; set; }

    [BindProperty]
    public string ExternalTermId { get; set; } = string.Empty;

    [BindProperty]
    public string ExternalOfferingId { get; set; } = string.Empty;

    [BindProperty]
    public string IdempotencyKey { get; set; } = string.Empty;

    [BindProperty]
    public string ExternalRegistrationId { get; set; } = string.Empty;

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(string? term = null) => await LoadAsync(term);

    public async Task<IActionResult> OnPostRefreshDegreeAuditAsync()
    {
        if (CurrentUser.Id is null)
            return Unauthorized();

        var profile = await _profiles.FindAsync(x => x.UserId == CurrentUser.Id.Value);
        var lookup = profile is null
            ? null
            : _degreeAuditLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (profile is null || lookup is null)
            return new JsonResult(new { refreshed = false }) { StatusCode = 404 };

        try
        {
            var audit = (await lookup.GetAuditsAsync(
                profile.ExternalStudentId, HttpContext.RequestAborted)).FirstOrDefault();
            if (audit is null)
                return new JsonResult(new { refreshed = false }) { StatusCode = 404 };

            await lookup.RefreshAuditAsync(profile.ExternalStudentId, audit.RevisionTermId,
                audit.AuditDegreeId, audit.AuditProgramId, HttpContext.RequestAborted);
            return new JsonResult(new { refreshed = true });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(exception,
                "Unable to refresh the current student's degree audit from Course Selection.");
            return new JsonResult(new { refreshed = false }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        try
        {
            await _courseSelection.AddAsync(new AddCourseSelectionInput
            {
                ExternalTermId = ExternalTermId,
                ExternalOfferingId = ExternalOfferingId,
                IdempotencyKey = IdempotencyKey
            });
            SuccessMessage = "The course was added to your schedule.";
            return RedirectToPage(new { term = ExternalTermId });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Unable to add course offering {OfferingId} for the current student.",
                ExternalOfferingId);
            ErrorTitle = "Course not added";
            ErrorMessage = exception.Message;
            await LoadAsync(ExternalTermId);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync()
    {
        try
        {
            await _courseSelection.RemoveAsync(new RemoveCourseSelectionInput
            {
                ExternalTermId = ExternalTermId,
                ExternalOfferingId = ExternalOfferingId,
                ExternalRegistrationId = ExternalRegistrationId,
                IdempotencyKey = IdempotencyKey
            });
            SuccessMessage = "The course was removed from your schedule.";
            return RedirectToPage(new { term = ExternalTermId });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Unable to remove course registration {RegistrationId} for the current student.",
                ExternalRegistrationId);
            ErrorTitle = "Course not removed";
            ErrorMessage = exception.Message;
            await LoadAsync(ExternalTermId);
            return Page();
        }
    }

    private async Task LoadAsync(string? requestedTermId = null)
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        if (CurrentUser.Id is null)
        {
            IsUnavailable = true;
            return;
        }

        var profile = await _profiles.FindAsync(x => x.UserId == CurrentUser.Id.Value);
        if (profile is null)
        {
            IsUnavailable = true;
            return;
        }

        StudentDisplayName = profile.DisplayName;
        StudentIdentifier = profile.StudentId;

        try
        {
            EligibleTerms = await _courseSelection.GetEligibleTermsAsync();
            if (EligibleTerms.Count == 0)
            {
                IsUnavailable = true;
                return;
            }

            var selectedTermId = requestedTermId;
            if (string.IsNullOrWhiteSpace(selectedTermId) ||
                EligibleTerms.All(x => x.ExternalTermId != selectedTermId))
            {
                var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
                var currentTerm = termLookup is null
                    ? null
                    : await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
                selectedTermId = currentTerm is not null &&
                                 EligibleTerms.Any(x => x.ExternalTermId == currentTerm.ExternalTermId)
                    ? currentTerm.ExternalTermId
                    : EligibleTerms[0].ExternalTermId;
            }

            ExternalTermId = selectedTermId;
            Selection = await _courseSelection.GetAsync(selectedTermId);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsUnavailable = true;
            _logger.LogWarning(exception, "Unable to load Course Selection for the current student.");
        }
    }
}
