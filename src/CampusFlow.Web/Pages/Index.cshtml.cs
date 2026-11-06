using CampusFlow.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Volo.Abp.MultiTenancy;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Students;
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
using CampusFlow.Web.BillApprovals;
using CampusFlow.CourseSelections;
using CampusFlow.Housing;
using CampusFlow.Announcements;
using CampusFlow.Web.Bookstores;

namespace CampusFlow.Web.Pages;

[Authorize]
public class IndexModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IStudentTermSelection _selectedTerm;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly IPermissionChecker _permissionChecker;
    private readonly StudentOnlyAccountAccess _studentOnlyAccountAccess;
    private readonly FacultyPortalReader _facultyReader;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly IdentityUserManager _userManager;
    private readonly ICourseSelectionAppService _courseSelection;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _registrationTermConfigurations;
    private readonly IMealPlanAppService _mealPlans;
    private readonly IRepository<BillApproval, Guid> _billApprovals;
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _billApprovalTermConfigurations;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemScheduleLookup> _scheduleLookups;
    private readonly IConfiguration _configuration;
    private readonly IRepository<PortalAnnouncement, Guid> _announcements;
    private readonly ILegacyBillApprovalLookup _legacyApprovals;
    private readonly IBillApprovalCompletionLookup _completion;

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
        StudentOnlyAccountAccess studentOnlyAccountAccess,
        FacultyPortalReader facultyReader,
        IStudentInformationSystemAdvisorLookup advisorLookup,
        IdentityUserManager userManager,
        ICourseSelectionAppService courseSelection,
        IRepository<RegistrationTermConfiguration, Guid> registrationTermConfigurations,
        IMealPlanAppService mealPlans,
        IRepository<BillApproval, Guid> billApprovals,
        IRepository<BillApprovalTermConfiguration, Guid> billApprovalTermConfigurations,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemScheduleLookup> scheduleLookups,
        IConfiguration configuration,
        IRepository<PortalAnnouncement, Guid> announcements,
        ILegacyBillApprovalLookup legacyApprovals, IBillApprovalCompletionLookup completion, IStudentTermSelection selectedTerm)
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
        _studentOnlyAccountAccess = studentOnlyAccountAccess;
        _facultyReader = facultyReader;
        _advisorLookup = advisorLookup;
        _userManager = userManager;
        _courseSelection = courseSelection;
        _registrationTermConfigurations = registrationTermConfigurations;
        _mealPlans = mealPlans;
        _billApprovals = billApprovals;
        _billApprovalTermConfigurations = billApprovalTermConfigurations;
        _billingLookups = billingLookups.ToArray();
        _scheduleLookups = scheduleLookups.ToArray();
        _configuration = configuration;
        _announcements = announcements;
        _legacyApprovals = legacyApprovals;
        _completion = completion;
        _selectedTerm = selectedTerm;
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
    public IReadOnlyList<DashboardTermOption> DashboardTerms { get; private set; } = [];
    public IReadOnlyList<BillApprovalTermOption> OpenBillApprovalTerms { get; private set; } = [];
    public IEnumerable<BillApprovalTermOption> OtherBillApprovalTerms =>
        OpenBillApprovalTerms.Where(x => x.ExternalTermId != CurrentTerm?.ExternalTermId);

    public bool HasStudentAccess { get; private set; }
    public bool HasAdvisorAccess { get; private set; }
    public int AdvisorStudentCount { get; private set; }
    public int AdvisorCourseCount { get; private set; }
    public RegistrationJourneyViewModel? RegistrationJourney { get; private set; }
    public IReadOnlyList<PortalAnnouncement> VisibleAnnouncements { get; private set; } = [];
    public IReadOnlyList<DashboardTermOption> AnnouncementTerms { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync([FromQuery] string? term = null)
    {
        TenantName = CurrentTenant.Name;
        Theme = _tenantThemeProvider.Get(TenantName);
        Portal = HttpContext.Items[DevelopmentPortalContextMiddleware.PortalItemKey] as PortalType?;

        if (CurrentUser.Id is null)
        {
            return Page();
        }

        HasAdvisorAccess = !_currentStudentView.IsImpersonating &&
            await _studentOnlyAccountAccess.HasStaffAccessAsync() &&
            await _advisorPortal.HasAccessAsync();

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (!_currentStudentView.IsImpersonating && !string.IsNullOrWhiteSpace(CurrentUser.Email))
        {
            try
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
                        var existing = await _studentProfileRepository.FindAsync(x =>
                            x.Provider == result.Student.Provider &&
                            x.ExternalStudentId == result.Student.ExternalStudentId);
                        if (existing is null)
                            profile = await _studentProfileRepository.InsertAsync(new StudentProfile(
                                _guidGenerator.Create(), CurrentTenant.Id,
                                CurrentUser.Id.Value, result.Student));
                        else
                        {
                            existing.AssignUser(CurrentUser.Id.Value);
                            existing.Update(result.Student);
                            profile = await _studentProfileRepository.UpdateAsync(existing);
                        }
                    }
                    else if (profile.Provider == result.Student.Provider &&
                             profile.ExternalStudentId == result.Student.ExternalStudentId)
                    {
                        profile.Update(result.Student);
                        profile = await _studentProfileRepository.UpdateAsync(profile);
                    }
                }
            }
            catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Unable to refresh the current student's Elements profile.");
            }
        }

        if (profile is not null)
        {
            HasStudentAccess = true;
            StudentIdentifier = profile.StudentId;
            StudentDisplayName = profile.DisplayName;

            var now = DateTime.UtcNow;
            var activeAnnouncements = (await _announcements.GetListAsync())
                .Where(x => x.IsPublished && x.StartsAtUtc <= now && now < x.EndsAtUtc &&
                    x.Audience.HasFlag(AnnouncementAudience.Student)).ToArray();

            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (termLookup is not null)
            {
                try
                {
                    var sisTerms = await termLookup.GetTermsAsync(HttpContext.RequestAborted);
                    BillApprovalTermConfiguration[] openBillTerms = [];
                    try
                    {
                        openBillTerms = (await _billApprovalTermConfigurations.GetListAsync())
                            .Where(x => x.IsOpenForStudent(Clock.Now, profile.StudentId) &&
                                sisTerms.Any(term => term.ExternalTermId == x.ExternalTermId))
                            .OrderByDescending(x => x.TermCode).ToArray();
                    }
                    catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                    {
                        _logger.LogWarning(exception, "Unable to load available bill-approval terms.");
                    }
                    OpenBillApprovalTerms = openBillTerms.Select(x => new BillApprovalTermOption(
                        x.ExternalTermId, x.TermCode, x.TermName)).ToArray();

                    var configuredTerms = (await _registrationTermConfigurations.GetListAsync())
                        .Where(x => x.IsStudentSelectable).OrderByDescending(x => x.TermCode).ToArray();
                    var configuredOptions = configuredTerms.Select(configuration =>
                    {
                        var sisTerm = sisTerms.FirstOrDefault(x => x.ExternalTermId == configuration.ExternalTermId);
                        return new DashboardTermOption(configuration.ExternalTermId, configuration.TermName,
                            sisTerm?.StartDate, sisTerm?.EndDate);
                    }).ToArray();
                    // An open bill-approval term remains reachable even when course selection
                    // is closed or its term is not selected for the registration dashboard.
                    var billOptions = openBillTerms
                        .Where(x => configuredOptions.All(option => option.ExternalTermId != x.ExternalTermId))
                        .Select(x =>
                        {
                            var sisTerm = sisTerms.Single(term => term.ExternalTermId == x.ExternalTermId);
                            return new DashboardTermOption(x.ExternalTermId, x.TermName,
                                sisTerm.StartDate, sisTerm.EndDate);
                        }).ToArray();
                    var registrationOptions = configuredOptions.Concat(billOptions).ToArray();
                    var announcementOptions = sisTerms.Where(sisTerm =>
                            activeAnnouncements.Any(announcement => announcement.Terms.Contains(
                                sisTerm.DisplayName, StringComparer.OrdinalIgnoreCase)) &&
                            registrationOptions.All(option => option.ExternalTermId != sisTerm.ExternalTermId))
                        .Select(sisTerm => new DashboardTermOption(sisTerm.ExternalTermId,
                            sisTerm.DisplayName, sisTerm.StartDate, sisTerm.EndDate));
                    DashboardTerms = registrationOptions.Concat(announcementOptions).ToArray();
                    CurrentTerm = await _selectedTerm.ResolveAsync(profile, termLookup,
                        DashboardTerms.Any(x => x.ExternalTermId == term) ? term : null,
                        configuredTerms.FirstOrDefault(x => x.IsDashboardDefault)?.ExternalTermId,
                        HttpContext.RequestAborted);
                    var selectedId = CurrentTerm?.ExternalTermId;
                    AnnouncementTerms = DashboardTerms.Where(option =>
                        option.ExternalTermId != selectedId && activeAnnouncements.Any(announcement =>
                            announcement.Terms.Contains(option.DisplayName, StringComparer.OrdinalIgnoreCase)))
                        .ToArray();
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Unable to resolve the current academic term.");
                }
            }

            VisibleAnnouncements = activeAnnouncements
                .Where(x => x.IsVisible(now, AnnouncementAudience.Student, CurrentTerm?.DisplayName))
                .OrderByDescending(x => x.StartsAtUtc).ToArray();

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

            var advisorTermId = (await _registrationTermConfigurations.GetListAsync())
                .Where(x => x.IsStudentSelectable)
                .OrderByDescending(x => x.IsDashboardDefault)
                .ThenByDescending(x => x.TermCode)
                .Select(x => x.ExternalTermId)
                .FirstOrDefault();
            var queue = string.IsNullOrWhiteSpace(advisorTermId)
                ? []
                : await _advisorPortal.GetQueueAsync(advisorTermId);
            AdvisorStudentCount = queue.Count;
            AdvisorCourseCount = queue.Sum(x => x.PendingCourseCount);
        }
        if (!HasStudentAccess && !HasAdvisorAccess && !_currentStudentView.IsImpersonating &&
            !string.IsNullOrWhiteSpace(CurrentUser.Email))
        {
            try
            {
                if (await _facultyReader.FindFacultyAsync(CurrentUser.Email, HttpContext.RequestAborted) is not null)
                    return RedirectToPage("/Faculty/Index");
            }
            catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Unable to resolve the faculty portal landing page.");
            }
        }

        return Page();
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
                var transactions = await billingLookup.GetTransactionsAsync(
                    profile.ExternalStudentId, HttpContext.RequestAborted);
                journey.AccountBalance = transactions
                    .Where(x => !x.IsVoided && string.CompareOrdinal(x.TermCode, term.TermCode) <= 0)
                    .Sum(x => x.BalanceChange);
            }
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to calculate registration-journey prior balance.");
        }

        try
        {
            var scheduleLookup = _scheduleLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (scheduleLookup is not null)
            {
                var scheduledCourses = (await scheduleLookup.GetScheduleAsync(
                    profile.ExternalStudentId, HttpContext.RequestAborted))
                    .Where(x => x.ExternalTermId == term.ExternalTermId).ToArray();
                journey.SelectedCourseCount = scheduledCourses.Length;
                journey.SelectedCredits = scheduledCourses.Sum(x => x.Credits);
                journey.HasSelectedCourses = scheduledCourses.Length > 0;
                journey.BookstoreUrl = CampusStoreLinks.ForCourses(
                    _configuration, CurrentTenant.Name, term.DisplayName, scheduledCourses);
            }
            var eligibleTerms = await _courseSelection.GetEligibleTermsAsync();
            journey.CourseSelectionEnabled = eligibleTerms.Any(x => x.ExternalTermId == term.ExternalTermId);
            if (journey.CourseSelectionEnabled)
            {
                var selection = await _courseSelection.GetAsync(term.ExternalTermId);
                // A course-selection response must not erase enrollment already reported
                // by the schedule, including courses entered directly in Elements.
                journey.HasSelectedCourses |= selection.Registrations.Count > 0;
                journey.IsAwaitingAdvisor = selection.Registrations.Any(x => x.NeedsReview);
            }
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey course status.");
        }

        try
        {
            var mealPlan = await _mealPlans.GetAsync(term.ExternalTermId);
            journey.HousingRequired = mealPlan.HousingRequired;
            journey.HasHousingAssignment = mealPlan.HasHousingAssignment;
            journey.MealPlanEligible = mealPlan.MealPlanEligible;
            journey.MealPlanEnabled = mealPlan.Options.Values.Any(x => x.Count > 0);
            journey.MealPlanRequired = journey.MealPlanEnabled;
            journey.MealPlanSelected = mealPlan.SelectedHousingChoice is not null;
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey meal-plan status.");
        }

        try
        {
            var billTerm = (await _billApprovalTermConfigurations.GetListAsync())
                .FirstOrDefault(x => x.ExternalTermId == term.ExternalTermId);
            journey.BillApprovalEnabled = billTerm?.IsOpenForStudent(Clock.Now, profile.StudentId) == true;
            journey.BillApprovalAvailability = billTerm?.Availability(Clock.Now, profile.StudentId) ?? "unavailable";
            journey.BillApproved = await _completion.IsApprovedAsync(profile, term.ExternalTermId, HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            journey.HasUnavailableData = true;
            _logger.LogWarning(exception, "Unable to load registration-journey bill-approval status.");
        }

        journey.MealPlanEnabled = journey.MealPlanEnabled && journey.BillApprovalEnabled;
        journey.Term = term;
        journey.BuildSteps();
        return journey;
    }

    public sealed class RegistrationJourneyViewModel
    {
        public decimal PreviousBalance { get; set; }
        public decimal AccountBalance { get; set; }
        public int SelectedCourseCount { get; set; }
        public decimal SelectedCredits { get; set; }
        public bool HasSelectedCourses { get; set; }
        public bool IsAwaitingAdvisor { get; set; }
        public bool CourseSelectionEnabled { get; set; }
        public bool HousingRequired { get; set; }
        public bool HasHousingAssignment { get; set; }
        public bool MealPlanEligible { get; set; }
        public bool MealPlanEnabled { get; set; }
        public bool MealPlanRequired { get; set; }
        public bool MealPlanSelected { get; set; }
        public bool BillApproved { get; set; }
        public bool BillApprovalEnabled { get; set; }
        public string BillApprovalAvailability { get; set; } = "unavailable";
        public bool HasUnavailableData { get; set; }
        public string? BookstoreUrl { get; set; }
        public List<RegistrationJourneyStep> Steps { get; } = [];
        public int CompletedCount => Steps.Count(x => x.State is "complete" or "not-required");
        public StudentInformationSystemTerm Term { get; set; } = null!;

        public void BuildSteps()
        {
            Steps.Clear();
            var courseUrl = $"/CourseSelection?term={Uri.EscapeDataString(Term.ExternalTermId)}";
            var billUrl = $"/BillApproval?term={Uri.EscapeDataString(Term.TermCode)}";
            if (BillApproved)
            {
                Steps.Add(new("Previous balance", "Bill approval is complete for this term.", "complete", null, "Complete"));
                Steps.Add(new("Course selection", "Course selection is locked after bill approval.", "complete", null, "Complete"));
                if (HousingRequired) Steps.Add(new("Housing", "Housing review is complete for this term.", "complete", null, "Complete"));
                if (MealPlanEligible) Steps.Add(new("Meal plan", "Meal plan selection is locked after bill approval.", "complete", null, "Complete"));
                Steps.Add(new("Bill approval", "Your bill has been reviewed and approved.", "complete", billUrl, "View agreement"));
                return;
            }
            Steps.Add(BillApprovalAccessRules.HasBlockingPreviousBalance(PreviousBalance)
                ? new("Previous balance", $"${PreviousBalance:N2} needs attention before bill approval.", "action", "/Billing", "Review balance")
                : new("Previous balance", "No prior balance is blocking bill approval.", "complete", "/Billing", "View account"));

            var viewCoursesUrl = CourseSelectionEnabled
                ? courseUrl
                : $"/Schedule?term={Uri.EscapeDataString(Term.ExternalTermId)}";
            Steps.Add(IsAwaitingAdvisor
                ? new("Course selection", "Your selected courses are awaiting advisor review.", "waiting", viewCoursesUrl, "View courses")
                : HasSelectedCourses
                    ? new("Course selection", "Courses have been selected for this term.", "complete", viewCoursesUrl, "View courses")
                    : !CourseSelectionEnabled
                        ? RegistrationJourneyStep.ComingSoon("Course selection")
                        : new("Course selection", "Choose the courses you plan to take.", "action", courseUrl, "Select courses"));

            var housingUrl = $"/Housing?term={Uri.EscapeDataString(Term.ExternalTermId)}";
            if (HousingRequired)
                Steps.Add(HasHousingAssignment
                    ? new("Housing", "Your housing assignment is recorded.", "complete", housingUrl, "View housing")
                    : new("Housing", "Contact Student Services for a housing assignment before approving your bill.", "action", housingUrl, "View housing"));

            if (MealPlanEligible)
            Steps.Add(!HasHousingAssignment
                ? new("Meal plan", "A housing assignment is required before choosing a meal plan.", "disabled", null, "Housing required")
                : !BillApprovalEnabled || !MealPlanEnabled
                ? UnavailableBillStep("Meal plan")
                : !MealPlanRequired
                ? new("Meal plan", "A meal-plan selection is not required for you.", "not-required", housingUrl, "View options")
                : MealPlanSelected
                    ? new("Meal plan", "Your housing and meal-plan choice has been recorded.", "complete", housingUrl, "View selection")
                    : new("Meal plan", "Choose the housing and meal-plan option that applies to you.", "action", housingUrl, "Choose plan"));

            Steps.Add(!BillApprovalEnabled
                ? UnavailableBillStep("Bill approval")
                : BillApproved
                ? new("Bill approval", "Your bill has been reviewed and approved.", "complete", billUrl, "View agreement")
                : BillApprovalAccessRules.HasBlockingPreviousBalance(PreviousBalance)
                ? new("Bill approval", "Your previous balance needs attention before bill approval.", "disabled", null, "Balance required")
                : HousingRequired && !HasHousingAssignment
                ? new("Bill approval", "A housing assignment is required before bill approval.", "disabled", null, "Housing required")
                : new("Bill approval", "Review your charges, payment choice, and agreement.", "action", billUrl, "Review bill"));
        }

        private RegistrationJourneyStep UnavailableBillStep(string title) => BillApprovalAvailability switch
        {
            "upcoming" => RegistrationJourneyStep.ComingSoon(title),
            "closed" => new(title, "The selection window for this term has closed.", "disabled", null, "Closed"),
            _ => new(title, "This step is not currently available for this term.", "disabled", null, "Unavailable")
        };
    }

    public sealed record RegistrationJourneyStep(
        string Title, string Description, string State, string? Url, string ActionLabel)
    {
        public static RegistrationJourneyStep ComingSoon(string title) =>
            new(title, "This step is not currently available.", "disabled", null, "Coming soon");
    }

    public sealed record BillApprovalTermOption(string ExternalTermId, string TermCode, string DisplayName);

    public sealed record DashboardTermOption(string ExternalTermId, string DisplayName,
        DateTime? StartDate, DateTime? EndDate);
}
