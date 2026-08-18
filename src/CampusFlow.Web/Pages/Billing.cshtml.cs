using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CampusFlow.Web.Payments;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages;

[Authorize]
public class BillingModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ILogger<BillingModel> _logger;
    private readonly PayflowOptions _payflow;

    public BillingModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        ILogger<BillingModel> logger, IOptions<PayflowOptions> payflow)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _currentStudentView = currentStudentView;
        _billingLookups = billingLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _logger = logger;
        _payflow = payflow.Value;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentDisplayName { get; private set; } = "Student";
    public string StudentIdentifier { get; private set; } = "Unavailable";
    public decimal PostedBalance { get; private set; }
    public decimal PendingBalance { get; private set; }
    public decimal OverallBalance => PostedBalance + PendingBalance;
    public bool IsBillingUnavailable { get; private set; }
    public IReadOnlyList<BillingTermGroup> Terms { get; private set; } = [];
    public IReadOnlyList<BillingTermGroup> CurrentAndUpcomingTerms { get; private set; } = [];
    public IReadOnlyList<BillingTermGroup> HistoricalTerms { get; private set; } = [];
    public string? CurrentTermCode { get; private set; }
    public bool CanMakePayment => _payflow.IsConfigured && !_currentStudentView.IsImpersonating && OverallBalance > 0;
    public bool IsPaymentTestMode => _payflow.TestMode;

    public string FormatCurrency(decimal amount) =>
        FormatCurrencyValue(amount);

    public static string FormatCurrencyValue(decimal amount) =>
        amount.ToString("$#,##0.00;($#,##0.00);$0.00");

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
            IsBillingUnavailable = true;
            return;
        }

        StudentDisplayName = profile.DisplayName;
        StudentIdentifier = profile.StudentId;

        var billingLookup = _billingLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (billingLookup is null)
        {
            IsBillingUnavailable = true;
            return;
        }

        try
        {
            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var currentTerm = termLookup is null
                ? null
                : await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            CurrentTermCode = currentTerm?.TermCode;
            var transactions = await billingLookup.GetTransactionsAsync(
                profile.ExternalStudentId,
                HttpContext.RequestAborted);

            var activeTransactions = transactions.Where(x => !x.IsVoided).ToArray();
            PostedBalance = activeTransactions.Where(x => !x.IsPending).Sum(x => x.BalanceChange);
            PendingBalance = activeTransactions.Where(x => x.IsPending).Sum(x => x.BalanceChange);

            Terms = CreateTermGroups(transactions);

            CurrentAndUpcomingTerms = currentTerm is null
                ? []
                : Terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) >= 0).ToArray();
            HistoricalTerms = currentTerm is null
                ? Terms
                : Terms.Where(x => string.CompareOrdinal(x.TermCode, currentTerm.TermCode) < 0).ToArray();
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsBillingUnavailable = true;
            _logger.LogWarning(exception, "Unable to load billing activity for the current student.");
        }
    }

    private static IReadOnlyList<BillingTermGroup> CreateTermGroups(
        IEnumerable<StudentBillingTransaction> transactions)
    {
        var groupedTransactions = transactions
                .GroupBy(x => new { x.TermCode, x.TermName })
                .OrderBy(group => group.Key.TermCode)
                .ToArray();
        var terms = new List<BillingTermGroup>(groupedTransactions.Length);
        decimal carriedBalance = 0;
        var isFirstTerm = true;

        foreach (var group in groupedTransactions)
        {
            var term = CreateTermGroup(
                group.Key.TermCode,
                group.Key.TermName,
                group,
                carriedBalance,
                !isFirstTerm);
            terms.Add(term);
            carriedBalance = term.Balance;
            isFirstTerm = false;
        }

        terms.Reverse();
        return terms;
    }

    private static BillingTermGroup CreateTermGroup(
        string termCode,
        string termName,
        IEnumerable<StudentBillingTransaction> transactions,
        decimal previousTermBalance,
        bool includeCarryForward)
    {
        var runningBalance = previousTermBalance;
        var rows = new List<BillingTransactionRow>();
        if (includeCarryForward && previousTermBalance != 0)
        {
            rows.Add(new BillingTransactionRow(null, previousTermBalance, true));
        }

        foreach (var transaction in transactions
                     .OrderBy(x => x.TransactionDate)
                     .ThenBy(x => x.ExternalTransactionId))
        {
            if (!transaction.IsVoided)
            {
                runningBalance += transaction.BalanceChange;
            }

            rows.Add(new BillingTransactionRow(transaction, runningBalance, false));
        }

        rows.Reverse();

        return new BillingTermGroup(
            termCode,
            termName,
            runningBalance,
            rows.Count(x => x.Transaction is { IsPending: true, IsVoided: false }),
            rows);
    }

    public sealed record BillingTermGroup(
        string TermCode,
        string TermName,
        decimal Balance,
        int PendingCount,
        IReadOnlyList<BillingTransactionRow> Transactions);

    public sealed record BillingTransactionRow(
        StudentBillingTransaction? Transaction,
        decimal RunningBalance,
        bool IsCarryForward);
}
