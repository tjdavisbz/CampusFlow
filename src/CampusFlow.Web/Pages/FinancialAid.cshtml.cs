using System;
using System.Collections.Generic;
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
public class FinancialAidModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemFinancialAidLookup> _awardLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemFinancialAidDecisionService> _decisionServices;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ILogger<FinancialAidModel> _logger;

    public FinancialAidModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemFinancialAidLookup> awardLookups,
        IEnumerable<IStudentInformationSystemFinancialAidDecisionService> decisionServices,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        ILogger<FinancialAidModel> logger)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _currentStudentView = currentStudentView;
        _awardLookups = awardLookups.ToArray();
        _decisionServices = decisionServices.ToArray();
        _termLookups = termLookups.ToArray();
        _logger = logger;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentDisplayName { get; private set; } = "Student";
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public bool IsUnavailable { get; private set; }
    public IReadOnlyList<AwardTermGroup> Terms { get; private set; } = [];
    public IReadOnlyList<AwardTermGroup> CurrentAndUpcomingTerms { get; private set; } = [];
    public IReadOnlyList<AwardTermGroup> HistoricalTerms { get; private set; } = [];
    public string? CurrentTermCode { get; private set; }
    public string? CurrentTermName { get; private set; }
    public decimal CurrentTermAwards =>
        Terms.SingleOrDefault(x => x.TermCode == CurrentTermCode)?.TotalAmount ?? 0m;
    [Microsoft.AspNetCore.Mvc.TempData]
    public string? StatusMessage { get; set; }
    [Microsoft.AspNetCore.Mvc.TempData]
    public bool StatusIsError { get; set; }

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
            IsUnavailable = true;
            return;
        }

        StudentDisplayName = profile.DisplayName;
        StudentIdentifier = profile.StudentId;
        var lookup = _awardLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (lookup is null)
        {
            IsUnavailable = true;
            return;
        }

        try
        {
            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var currentTerm = termLookup is null
                ? null
                : await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            CurrentTermCode = currentTerm?.TermCode;
            CurrentTermName = currentTerm?.DisplayName;

            var awards = await lookup.GetAwardsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            var decisionService = _decisionServices.SingleOrDefault(x => x.Provider == profile.Provider);
            if (decisionService is not null)
            {
                try
                {
                    var decisions = await decisionService.GetDecisionsAsync(
                        profile.ExternalStudentId, HttpContext.RequestAborted);
                    awards = awards.Select(award => decisions.TryGetValue(award.ExternalAwardId, out var decision)
                        ? award with { StudentAccepted = decision }
                        : award).ToArray();
                }
                catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning(exception,
                        "Unable to refresh financial aid decision statuses from Elements API.");
                }
            }
            Terms = awards
                .GroupBy(x => new { x.TermCode, x.TermName })
                .OrderByDescending(x => x.Key.TermCode)
                .Select(group => new AwardTermGroup(
                    group.Key.TermCode,
                    group.Key.TermName,
                    group.Sum(x => x.Amount),
                    group.OrderByDescending(x => x.AwardDate).ThenBy(x => x.Description).ToArray()))
                .ToArray();
            CurrentAndUpcomingTerms = currentTerm is null
                ? []
                : Terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) >= 0).ToArray();
            HistoricalTerms = currentTerm is null
                ? Terms
                : Terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) < 0).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsUnavailable = true;
            _logger.LogWarning(exception, "Unable to load financial aid for the current student.");
        }
    }

    public static string FormatCurrency(decimal amount) =>
        amount.ToString("$#,##0.00;($#,##0.00);$0.00");

    public async Task<Microsoft.AspNetCore.Mvc.IActionResult> OnPostDecisionAsync(
        string awardId,
        StudentFinancialAidDecision decision)
    {
        if (CurrentUser.Id is null ||
            decision is not (StudentFinancialAidDecision.Accept or StudentFinancialAidDecision.Decline))
        {
            return Forbid();
        }

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        var service = profile is null
            ? null
            : _decisionServices.SingleOrDefault(x => x.Provider == profile.Provider);
        if (profile is null || service is null)
        {
            StatusIsError = true;
            StatusMessage = "Your financial aid decision could not be submitted.";
            return RedirectToPage();
        }

        try
        {
            await service.SubmitDecisionAsync(profile.ExternalStudentId, awardId, decision,
                HttpContext.RequestAborted);
            StatusMessage = decision == StudentFinancialAidDecision.Accept
                ? "Your award was accepted successfully."
                : "Your award was declined successfully.";
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            StatusIsError = true;
            StatusMessage = "Elements could not record your decision. Nothing was changed; please try again.";
            _logger.LogWarning(exception,
                "Unable to submit financial aid decision for award {AwardId} and user {UserId}.",
                awardId, CurrentUser.Id);
        }

        return RedirectToPage();
    }

    public sealed record AwardTermGroup(
        string TermCode,
        string TermName,
        decimal TotalAmount,
        IReadOnlyList<StudentFinancialAidAward> Awards);
}
