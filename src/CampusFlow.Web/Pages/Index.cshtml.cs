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
    private readonly IRepository<RegistrationTermConfiguration, Guid> _registrationTermConfigurations;
    private readonly IMealPlanAppService _mealPlans;
    private readonly IRepository<BillApproval, Guid> _billApprovals;
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _billApprovalTermConfigurations;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemScheduleLookup> _scheduleLookups;
    private readonly IConfiguration _configuration;

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
        IRepository<RegistrationTermConfiguration, Guid> registrationTermConfigurations,
        IMealPlanAppService mealPlans,
        IRepository<BillApproval, Guid> billApprovals,
        IRepository<BillApprovalTermConfiguration, Guid> billApprovalTermConfigurations,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemScheduleLookup> scheduleLookups,
        IConfiguration configuration)
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
        _registrationTermConfigurations = registrationTermConfigurations;
        _mealPlans = mealPlans;
        _billApprovals = billApprovals;
        _billApprovalTermConfigurations = billApprovalTermConfigurations;
        _billingLookups = billingLookups.ToArray();
        _scheduleLookups = scheduleLookups.ToArray();
        _configuration = configuration;
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
    public bool HasStudentAccess { get; private set; }
    public bool HasAdvisorAccess { get; private set; }
    public int AdvisorStudentCount { get; private set; }
    public int AdvisorCourseCount { get; private set; }
    public RegistrationJourneyViewModel? RegistrationJourney { get; private set; }

    public async Task OnGetAsync([FromQuery] string? term = null)
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
                    var sisTerms = await termLookup.GetTermsAsync(HttpContext.RequestAborted);
                    var configuredTerms = (await _registrationTermConfigurations.GetListAsync())
                        .Where(x => x.IsStudentSelectable).OrderByDescending(x => x.TermCode).ToArray();
                    DashboardTerms = configuredTerms.Select(configuration =>
                    {
                        var sisTerm = sisTerms.FirstOrDefault(x => x.ExternalTermId == configuration.ExternalTermId);
                        return new DashboardTermOption(configuration.ExternalTermId, configuration.TermName,
                            sisTerm?.StartDate, sisTerm?.EndDate);
                    }).ToArray();
                    var selectedId = !string.IsNullOrWhiteSpace(term) && DashboardTerms.Any(x => x.ExternalTermId == term)
                        ? term
                        : configuredTerms.FirstOrDefault(x => x.IsDashboardDefault)?.ExternalTermId;
                    selectedId ??= (await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted))?.ExternalTermId;
                    selectedId ??= DashboardTerms.FirstOrDefault()?.ExternalTermId;
                    CurrentTerm = sisTerms.FirstOrDefault(x => x.ExternalTermId == selectedId);
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
                journey.BookstoreUrl = BuildBookstoreUrl(term, scheduledCourses);
            }
            var eligibleTerms = await _courseSelection.GetEligibleTermsAsync();
            journey.CourseSelectionEnabled = eligibleTerms.Any(x => x.ExternalTermId == term.ExternalTermId);
            if (journey.CourseSelectionEnabled)
            {
                var selection = await _courseSelection.GetAsync(term.ExternalTermId);
                journey.HasSelectedCourses = selection.Registrations.Count > 0;
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
            var mealPlan = await _mealPlans.GetAsync();
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
                .FirstOrDefault(x => x.ExternalTermId == term.ExternalTermId && x.IsOpen(Clock.Now));
            journey.BillApprovalEnabled = billTerm is not null;
            var approval = billTerm is null
                ? null
                : await _billApprovals.FirstOrDefaultAsync(x =>
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

        journey.MealPlanEnabled = journey.MealPlanEnabled && journey.BillApprovalEnabled;
        journey.Term = term;
        journey.BuildSteps();
        return journey;
    }

    private string? BuildBookstoreUrl(StudentInformationSystemTerm term,
        IReadOnlyCollection<StudentCourseScheduleItem> courses)
    {
        var tenantKey = CurrentTenant.Name ?? "Default";
        var section = _configuration.GetSection($"CampusStores:{tenantKey}");
        var baseUrl = section["CourseSelectionUrl"];
        var storeId = section["StoreId"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(storeId)) return null;

        var offeringIds = courses.Select(x => x.ExternalCourseOfferingId)
            .Where(x => long.TryParse(x, out _))
            .Select(x => x.PadLeft(15, '0'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (offeringIds.Length == 0) return null;

        var displayParts = term.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bookstoreTerm = displayParts.Length > 1 && displayParts[^1].Length >= 2
            ? $"{string.Join(' ', displayParts[..^1])} {displayParts[^1][^2..]}"
            : term.DisplayName;
        return $"{baseUrl}?src=2&type=2&stoid={Uri.EscapeDataString(storeId)}" +
               $"&trm={Uri.EscapeDataString(bookstoreTerm)}&cid={string.Join(',', offeringIds)}";
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
        public bool MealPlanEnabled { get; set; }
        public bool MealPlanRequired { get; set; }
        public bool MealPlanSelected { get; set; }
        public bool BillApproved { get; set; }
        public bool BillApprovalEnabled { get; set; }
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
            Steps.Add(PreviousBalance > 50
                ? new("Previous balance", $"${PreviousBalance:N2} needs attention before registration.", "action", "/Billing", "Review balance")
                : new("Previous balance", "No prior balance is blocking registration.", "complete", "/Billing", "View account"));

            Steps.Add(!CourseSelectionEnabled
                ? RegistrationJourneyStep.ComingSoon("Course selection")
                : IsAwaitingAdvisor
                ? new("Course selection", "Your selected courses are awaiting advisor review.", "waiting", courseUrl, "View courses")
                : HasSelectedCourses
                    ? new("Course selection", "Courses have been selected for this term.", "complete", courseUrl, "View courses")
                    : new("Course selection", "Choose the courses you plan to take.", "action", courseUrl, "Select courses"));

            Steps.Add(!BillApprovalEnabled || !MealPlanEnabled
                ? RegistrationJourneyStep.ComingSoon("Meal plan")
                : !MealPlanRequired
                ? new("Meal plan", "A meal-plan selection is not required for you.", "not-required", "/Housing", "View options")
                : MealPlanSelected
                    ? new("Meal plan", "Your housing and meal-plan choice has been recorded.", "complete", "/Housing", "View selection")
                    : new("Meal plan", "Choose the housing and meal-plan option that applies to you.", "action", "/Housing", "Choose plan"));

            Steps.Add(!BillApprovalEnabled
                ? RegistrationJourneyStep.ComingSoon("Bill approval")
                : BillApproved
                ? new("Bill approval", "Your bill has been reviewed and approved.", "complete", billUrl, "View agreement")
                : new("Bill approval", "Review your charges, payment choice, and agreement.", "action", billUrl, "Review bill"));
        }
    }

    public sealed record RegistrationJourneyStep(
        string Title, string Description, string State, string? Url, string ActionLabel)
    {
        public static RegistrationJourneyStep ComingSoon(string title) =>
            new(title, "This step is not currently available.", "disabled", null, "Coming soon");
    }

    public sealed record DashboardTermOption(string ExternalTermId, string DisplayName,
        DateTime? StartDate, DateTime? EndDate);
}
