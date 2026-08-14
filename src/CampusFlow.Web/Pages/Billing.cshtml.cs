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
public class BillingModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ILogger<BillingModel> _logger;

    public BillingModel(
        ITenantThemeProvider tenantThemeProvider,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        ILogger<BillingModel> logger)
    {
        _tenantThemeProvider = tenantThemeProvider;
        _studentProfileRepository = studentProfileRepository;
        _billingLookups = billingLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _logger = logger;
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

        var profile = await _studentProfileRepository.FindAsync(x => x.UserId == CurrentUser.Id.Value);
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

            Terms = transactions
                .GroupBy(x => new { x.TermCode, x.TermName })
                .OrderByDescending(group => group.Key.TermCode)
                .Select(group => CreateTermGroup(group.Key.TermCode, group.Key.TermName, group))
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
            IsBillingUnavailable = true;
            _logger.LogWarning(exception, "Unable to load billing activity for the current student.");
        }
    }

    private static BillingTermGroup CreateTermGroup(
        string termCode,
        string termName,
        IEnumerable<StudentBillingTransaction> transactions)
    {
        decimal runningBalance = 0;
        var rows = transactions
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.ExternalTransactionId)
            .Select(transaction =>
            {
                if (!transaction.IsVoided)
                {
                    runningBalance += transaction.BalanceChange;
                }

                return new BillingTransactionRow(transaction, runningBalance);
            })
            .Reverse()
            .ToArray();

        return new BillingTermGroup(
            termCode,
            termName,
            runningBalance,
            rows.Count(x => x.Transaction.IsPending && !x.Transaction.IsVoided),
            rows);
    }

    public sealed record BillingTermGroup(
        string TermCode,
        string TermName,
        decimal Balance,
        int PendingCount,
        IReadOnlyList<BillingTransactionRow> Transactions);

    public sealed record BillingTransactionRow(
        StudentBillingTransaction Transaction,
        decimal RunningBalance);
}
