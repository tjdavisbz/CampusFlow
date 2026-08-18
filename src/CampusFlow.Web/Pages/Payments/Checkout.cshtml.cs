using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Payments;

[Authorize]
public class CheckoutModel : CampusFlowPageModel
{
    private readonly ICurrentStudentView _studentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemPaymentPostingService> _paymentPostingServices;
    private readonly IRepository<PayflowPayment, Guid> _payments;
    private readonly IPayflowGateway _gateway;
    private readonly PayflowOptions _options;
    private readonly ILogger<CheckoutModel> _logger;

    public CheckoutModel(ICurrentStudentView studentView,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IEnumerable<IStudentInformationSystemPaymentPostingService> paymentPostingServices,
        IRepository<PayflowPayment, Guid> payments, IPayflowGateway gateway,
        IOptions<PayflowOptions> options, ILogger<CheckoutModel> logger)
    {
        _studentView = studentView; _billingLookups = billingLookups.ToArray();
        _termLookups = termLookups.ToArray(); _paymentPostingServices = paymentPostingServices.ToArray();
        _payments = payments;
        _gateway = gateway; _options = options.Value; _logger = logger;
    }

    [BindProperty, Range(typeof(decimal), "1.00", "999999999.99")]
    public decimal Amount { get; set; }
    [BindProperty, Required, CreditCard, Display(Name = "Card number")]
    public string CardNumber { get; set; } = string.Empty;
    [BindProperty, Range(1, 12), Display(Name = "Expiration month")]
    public int ExpirationMonth { get; set; }
    [BindProperty, Range(2026, 2100), Display(Name = "Expiration year")]
    public int ExpirationYear { get; set; }
    [BindProperty, Required, RegularExpression(@"^\d{3,4}$"), Display(Name = "Security code")]
    public string SecurityCode { get; set; } = string.Empty;
    [BindProperty, StringLength(20), Display(Name = "Billing ZIP/postal code")]
    public string? PostalCode { get; set; }
    public decimal OutstandingBalance { get; private set; }
    public bool IsTestMode => _options.TestMode;
    public string? ResultTitle { get; private set; }
    public string? ResultMessage { get; private set; }
    public bool WasApproved { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var profile = await RequireWritableStudentAsync();
        if (profile is null) return RedirectToPage("/Billing");
        await RetryPendingElementsPaymentsAsync(profile);
        OutstandingBalance = await LoadBalanceAsync(profile);
        Amount = Math.Max(0, OutstandingBalance);
        ExpirationMonth = DateTime.UtcNow.Month;
        ExpirationYear = DateTime.UtcNow.Year;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var profile = await RequireWritableStudentAsync();
        if (profile is null) return RedirectToPage("/Billing");
        OutstandingBalance = await LoadBalanceAsync(profile);
        if (!_options.IsConfigured) ModelState.AddModelError(string.Empty, "Online payments are not configured yet.");
        if (OutstandingBalance <= 0) ModelState.AddModelError(string.Empty, "There is no balance due.");
        if (Amount > OutstandingBalance) ModelState.AddModelError(nameof(Amount), "The payment cannot exceed your current balance.");
        var now = DateTime.UtcNow;
        if (ExpirationYear < now.Year || ExpirationYear == now.Year && ExpirationMonth < now.Month)
            ModelState.AddModelError(nameof(ExpirationMonth), "Enter a future expiration date.");
        if (!ModelState.IsValid)
        {
            ClearSensitiveFields();
            return Page();
        }

        var id = GuidGenerator.Create();
        var tokenId = Guid.NewGuid().ToString("N");
        var payment = new PayflowPayment(id, CurrentTenant.Id, CurrentUser.Id!.Value, profile.Id,
            profile.ExternalStudentId, Amount, _options.Currency, tokenId, _options.TestMode);
        await _payments.InsertAsync(payment, autoSave: true);
        try
        {
            var number = Regex.Replace(CardNumber, @"[\s-]", string.Empty);
            var response = await _gateway.SaleAsync(Amount, $"CF{id:N}"[..20],
                new PayflowCard(number, ExpirationMonth, ExpirationYear, SecurityCode, PostalCode),
                HttpContext.RequestAborted);
            _logger.LogInformation(
                "Payflow sale completed for payment {PaymentId}. Result={GatewayResult}, Message={GatewayMessage}, Reference={GatewayReference}",
                id, response.Result, response.Message, response.Reference);
            payment.Complete(response.IsDirectSaleApproved, response.Result, response.Message, response.Reference);
            await _payments.UpdateAsync(payment, autoSave: true);
            WasApproved = response.IsDirectSaleApproved;
            var accountUpdatePending = false;
            if (WasApproved)
            {
                try
                {
                    var termLookup = _termLookups.Single(x => x.Provider == profile.Provider);
                    var postingService = _paymentPostingServices.Single(x => x.Provider == profile.Provider);
                    var term = await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted)
                               ?? throw new InvalidOperationException("No current Elements term is configured.");
                    if (!int.TryParse(term.ExternalTermId, out var termCalendarId))
                        throw new InvalidOperationException("The current Elements term identifier is invalid.");
                    var posting = await postingService.PostAsync(profile.ExternalStudentId, termCalendarId,
                        payment.Amount, response.Reference ?? string.Empty, payment.IsTest, DateTime.UtcNow,
                        HttpContext.RequestAborted);
                    payment.MarkElementsPosted(posting.BatchMasterId, posting.BillingBatchId);
                    await _payments.UpdateAsync(payment, autoSave: true);
                    _logger.LogInformation(
                        "Posted Payflow payment {PaymentId} to Elements batch {BatchMasterId}, entry {BillingBatchId}",
                        id, posting.BatchMasterId, posting.BillingBatchId);
                }
                catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    accountUpdatePending = true;
                    payment.MarkElementsPostingFailed(ex.Message);
                    await _payments.UpdateAsync(payment, autoSave: true);
                    _logger.LogError(ex,
                        "Payflow payment {PaymentId} was approved but could not be posted to Elements", id);
                }
            }
            ResultTitle = WasApproved ? "Payment received" : "Payment was not approved";
            ResultMessage = WasApproved
                ? accountUpdatePending
                    ? $"Your payment of {payment.Amount:C} was approved. Reference: {response.Reference}. Your student account will update shortly."
                    : $"Your payment of {payment.Amount:C} was approved and added to your student account. Reference: {response.Reference}"
                : "No payment was made. Please review your card details or try another payment method.";
            ClearSensitiveFields();
            return Page();
        }
        catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            payment.Fail(null, "Gateway unavailable"); await _payments.UpdateAsync(payment, autoSave: true);
            _logger.LogWarning(ex, "Unable to initiate Payflow payment {PaymentId}", id);
            ModelState.AddModelError(string.Empty, "PayPal is temporarily unavailable. No payment was made.");
            ClearSensitiveFields();
            return Page();
        }
    }

    private async Task<StudentProfile?> RequireWritableStudentAsync()
    {
        if (_studentView.IsImpersonating || CurrentUser.Id is null || !_options.Enabled) return null;
        return await _studentView.GetProfileAsync(HttpContext.RequestAborted);
    }

    private async Task<decimal> LoadBalanceAsync(StudentProfile profile)
    {
        var lookup = _billingLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        if (lookup is null) return 0;
        var items = await lookup.GetTransactionsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
        return items.Where(x => !x.IsVoided).Sum(x => x.BalanceChange);
    }

    private async Task RetryPendingElementsPaymentsAsync(StudentProfile profile)
    {
        var pending = await _payments.GetListAsync(x =>
            x.StudentProfileId == profile.Id &&
            x.Status == PayflowPaymentStatus.Approved &&
            (x.ElementsPostingStatus == ElementsPaymentPostingStatus.Pending ||
             x.ElementsPostingStatus == ElementsPaymentPostingStatus.Failed));
        if (pending.Count == 0) return;

        var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
        var postingService = _paymentPostingServices.SingleOrDefault(x => x.Provider == profile.Provider);
        if (termLookup is null || postingService is null) return;
        var term = await termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
        if (term is null || !int.TryParse(term.ExternalTermId, out var termCalendarId)) return;

        foreach (var payment in pending.Where(x => !string.IsNullOrWhiteSpace(x.PayflowReference)))
        {
            try
            {
                var result = await postingService.PostAsync(payment.ExternalStudentId, termCalendarId,
                    payment.Amount, payment.PayflowReference!, payment.IsTest, payment.CreationTime,
                    HttpContext.RequestAborted);
                payment.MarkElementsPosted(result.BatchMasterId, result.BillingBatchId);
                await _payments.UpdateAsync(payment, autoSave: true);
            }
            catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                payment.MarkElementsPostingFailed(ex.Message);
                await _payments.UpdateAsync(payment, autoSave: true);
                _logger.LogWarning(ex, "Could not retry Elements posting for payment {PaymentId}", payment.Id);
            }
        }
    }

    private void ClearSensitiveFields()
    {
        var cardErrors = ModelState.TryGetValue(nameof(CardNumber), out var cardState)
            ? cardState.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            : [];
        var securityCodeErrors = ModelState.TryGetValue(nameof(SecurityCode), out var securityCodeState)
            ? securityCodeState.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            : [];
        CardNumber = string.Empty;
        SecurityCode = string.Empty;
        ModelState.Remove(nameof(CardNumber));
        ModelState.Remove(nameof(SecurityCode));
        foreach (var error in cardErrors) ModelState.AddModelError(nameof(CardNumber), error);
        foreach (var error in securityCodeErrors) ModelState.AddModelError(nameof(SecurityCode), error);
    }
}
