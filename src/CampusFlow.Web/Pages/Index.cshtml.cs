using CampusFlow.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using CampusFlow.AdvisorPortal;
using CampusFlow.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using CampusFlow.BillApprovals;
using CampusFlow.CourseSelections;
using CampusFlow.Housing;

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly IdentityUserManager _userManager;
    private readonly ICourseSelectionAppService _courseSelection;
    private readonly IMealPlanAppService _mealPlans;
    private readonly IRepository<BillApproval, Guid> _billApprovals;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;

    public IndexModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IGuidGenerator guidGenerator,
        ILogger<IndexModel> logger,
        IAdvisorPortalAppService advisorPortal,
        IPermissionChecker permissionChecker,
        IStudentInformationSystemAdvisorLookup advisorLookup,
        IdentityUserManager userManager,
        ICourseSelectionAppService courseSelection,
        IMealPlanAppService mealPlans,
        IRepository<BillApproval, Guid> billApprovals,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _currentStudentView = currentStudentView;
        _studentLookups = studentLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _guidGenerator = guidGenerator;
        _logger = logger;
        _advisorPortal = advisorPortal;
        _permissionChecker = permissionChecker;
        _advisorLookup = advisorLookup;
        _userManager = userManager;
        _courseSelection = courseSelection;
        _mealPlans = mealPlans;
        _billApprovals = billApprovals;
        _billingLookups = billingLookups.ToArray();
    }

    public string? TenantName { get; private set; }
    public PortalType? Portal { get; private set; }
    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string InstitutionName => Theme.OrganizationName;
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public string StudentDisplayName { get; private set; } = "Student";
    public StudentInformationSystemTerm? CurrentTerm { get; private set; }
    public bool HasStudentAccess { get; private set; }
    public bool HasAdvisorAccess { get; private set; }
    public int AdvisorStudentCount { get; private set; }
    public int AdvisorCourseCount { get; private set; }
    public RegistrationJourneyViewModel? RegistrationJourney { get; private set; }

    public async Task OnGetAsync()
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;

        if (CurrentUser.Id is null)
        {
            return;
        }

        HasAdvisorAccess = !_currentStudentView.IsImpersonating && await _permissionChecker.IsGrantedAsync(
            CampusFlowPermissions.AdvisorPortal.Default);

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (!_currentStudentView.IsImpersonating && !string.IsNullOrWhiteSpace(CurrentUser.Email))
        {
            var lookup = _studentLookups.SingleOrDefault(x =>
                x.Provider == (profile?.Provider ?? StudentInformationSystemProvider.ThesisElements));
            var result = lookup is null
                ? null
                : await lookup.FindByEmailAsync(CurrentUser.Email, HttpContext.RequestAborted);

            if (result?.Status == StudentLookupStatus.Matched && result.Student is not null)
            {
                if (profile is null)
                {
                    profile = await _studentProfileRepository.InsertAsync(new StudentProfile(
                        _guidGenerator.Create(),
                        CurrentTenant.Id,
                        CurrentUser.Id.Value,
                        result.Student));
                }
                else if (profile.Provider == result.Student.Provider &&
                         profile.ExternalStudentId == result.Student.ExternalStudentId)
                {
                    profile.Update(result.Student);
                    profile = await _studentProfileRepository.UpdateAsync(profile);
                }
            }
        }

        if (profile is not null)
        {
            HasStudentAccess = true;
            StudentIdentifier = profile.StudentId;
            StudentDisplayName = profile.DisplayName;

            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (termLookup is not null)
            {
                try
                {
                    CurrentTerm = await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Unable to resolve the current academic term.");
                }
            }

            if (CurrentTerm is not null)
            {
                RegistrationJourney = await BuildRegistrationJourneyAsync(profile, CurrentTerm);
            }
        }
        else
        {
            StudentDisplayName = CurrentUser.Name ?? CurrentUser.UserName ?? "Advisor";
        }

        if (HasAdvisorAccess)
        {
            if (!string.IsNullOrWhiteSpace(CurrentUser.Email))
            {
                try
                {
                    var advisor = await _advisorLookup.FindAsync(
                        CurrentUser.Email, HttpContext.RequestAborted);
                    if (advisor is not null)
                    {
                        StudentDisplayName = advisor.DisplayName;
                        var user = await _userManager.GetByIdAsync(CurrentUser.Id.Value);
                        if (!string.Equals(user.Name, advisor.FirstName, StringComparison.Ordinal) ||
                            !string.Equals(user.Surname, advisor.LastName, StringComparison.Ordinal))
                        {
                            user.Name = advisor.FirstName;
                            user.Surname = advisor.LastName;
                            var update = await _userManager.UpdateAsync(user);
                            if (!update.Succeeded)
                            {
                                _logger.LogWarning("Unable to refresh the advisor's display name from Elements.");
                            }
                        }
                    }
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Unable to refresh the advisor identity from Elements.");
                }
            }

            var queue = await _advisorPortal.GetQueueAsync();
            AdvisorStudentCount = queue.Count;
            AdvisorCourseCount = queue.Sum(x => x.PendingCourseCount);
        }
    }

    private async Task<RegistrationJourneyViewModel> BuildRegistrationJourneyAsync(
        StudentProfile profile,
        StudentInformationSystemTerm term)
    {
        var journey = new RegistrationJourneyViewModel();

        try
        {
            var billingLookup = _billingLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (billingLookup is not null)
            {
                journey.PreviousBalance = await billingLookup.GetPreviousBalanceAsync(
                    profile.ExternalStudentId, term.ExternalTermId, HttpContext.RequestAborted);
            }
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to calculate registration-journey prior balance.");
        }

        try
        {
            var selection = await _courseSelection.GetAsync(term.ExternalTermId);
            journey.HasSelectedCourses = selection.Registrations.Count > 0;
            journey.IsAwaitingAdvisor = selection.Registrations.Any(x => x.NeedsReview);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey course status.");
        }

        try
        {
            var mealPlan = await _mealPlans.GetAsync();
            journey.MealPlanRequired = mealPlan.Options.Values.Any(x => x.Count > 0);
            journey.MealPlanSelected = mealPlan.SelectedHousingChoice is not null;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey meal-plan status.");
        }

        try
        {
            var approval = await _billApprovals.FirstOrDefaultAsync(x =>
                x.StudentProfileId == profile.Id && x.ExternalTermId == term.ExternalTermId);
            journey.BillApproved = approval?.Status is BillApprovalStatus.Approved
                or BillApprovalStatus.DocumentPending
                or BillApprovalStatus.Completed;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey bill-approval status.");
        }

        journey.BuildSteps();
        return journey;
    }

    public sealed class RegistrationJourneyViewModel
    {
        public decimal PreviousBalance { get; set; }
        public bool HasSelectedCourses { get; set; }
        public bool IsAwaitingAdvisor { get; set; }
        public bool MealPlanRequired { get; set; }
        public bool MealPlanSelected { get; set; }
        public bool BillApproved { get; set; }
        public bool HasUnavailableData { get; set; }
        public List<RegistrationJourneyStep> Steps { get; } = [];
        public int CompletedCount => Steps.Count(x => x.State is "complete" or "not-required");

        public void BuildSteps()
        {
            Steps.Clear();
            Steps.Add(PreviousBalance > 50
                ? new("Previous balance", $"${PreviousBalance:N2} needs attention before registration.", "action", "/Billing", "Review balance")
                : new("Previous balance", "No prior balance is blocking registration.", "complete", "/Billing", "View account"));

            Steps.Add(IsAwaitingAdvisor
                ? new("Course selection", "Your selected courses are awaiting advisor review.", "waiting", "/CourseSelection", "View courses")
                : HasSelectedCourses
                    ? new("Course selection", "Courses have been selected for this term.", "complete", "/CourseSelection", "View courses")
                    : new("Course selection", "Choose the courses you plan to take.", "action", "/CourseSelection", "Select courses"));

            Steps.Add(!MealPlanRequired
                ? new("Meal plan", "A meal-plan selection is not required for you.", "not-required", "/Housing", "View options")
                : MealPlanSelected
                    ? new("Meal plan", "Your housing and meal-plan choice has been recorded.", "complete", "/Housing", "View selection")
                    : new("Meal plan", "Choose the housing and meal-plan option that applies to you.", "action", "/Housing", "Choose plan"));

            Steps.Add(BillApproved
                ? new("Bill approval", "Your bill has been reviewed and approved.", "complete", "/BillApproval", "View agreement")
                : new("Bill approval", "Review your charges, payment choice, and agreement.", "action", "/BillApproval", "Review bill"));
        }
    }

    public sealed record RegistrationJourneyStep(
        string Title, string Description, string State, string Url, string ActionLabel);
}
