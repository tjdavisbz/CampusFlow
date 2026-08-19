using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.BlobStoring;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace CampusFlow.Web.Pages;

[Authorize]
public class BillApprovalModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemScheduleLookup> _scheduleLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingLookup> _billingLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemFinancialAidLookup> _aidLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemPaymentPlanLookup> _paymentPlanLookups;
    private readonly IReadOnlyCollection<IStudentInformationSystemDocumentTrackingService> _documentTrackingServices;
    private readonly IReadOnlyCollection<IStudentInformationSystemPaymentPostingService> _paymentPostingServices;
    private readonly IConfiguration _configuration;
    private readonly IRepository<AgreementTemplate, Guid> _agreementTemplates;
    private readonly IRepository<PaymentPlanPolicy, Guid> _paymentPlanPolicies;
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _termConfigurations;
    private readonly IRepository<BillApproval, Guid> _billApprovals;
    private readonly IRepository<BillApprovalArtifact, Guid> _artifacts;
    private readonly IBlobContainer<BillApprovalPdfContainer> _pdfs;
    private readonly IBillApprovalPdfGenerator _pdfGenerator;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly IRepository<PayflowPayment, Guid> _payflowPayments;
    private readonly IPayflowGateway _payflowGateway;
    private readonly PayflowOptions _payflowOptions;
    private readonly ILogger<BillApprovalModel> _logger;

    public BillApprovalModel(
        ITenantThemeProvider themeProvider,
        IRepository<StudentProfile, Guid> profiles,
        ICurrentStudentView currentStudentView,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IEnumerable<IStudentInformationSystemScheduleLookup> scheduleLookups,
        IEnumerable<IStudentInformationSystemBillingLookup> billingLookups,
        IEnumerable<IStudentInformationSystemFinancialAidLookup> aidLookups,
        IEnumerable<IStudentInformationSystemPaymentPlanLookup> paymentPlanLookups,
        IEnumerable<IStudentInformationSystemDocumentTrackingService> documentTrackingServices,
        IEnumerable<IStudentInformationSystemPaymentPostingService> paymentPostingServices,
        IConfiguration configuration,
        IRepository<AgreementTemplate, Guid> agreementTemplates,
        IRepository<PaymentPlanPolicy, Guid> paymentPlanPolicies,
        IRepository<BillApprovalTermConfiguration, Guid> termConfigurations,
        IRepository<BillApproval, Guid> billApprovals,
        IRepository<BillApprovalArtifact, Guid> artifacts,
        IBlobContainer<BillApprovalPdfContainer> pdfs,
        IBillApprovalPdfGenerator pdfGenerator,
        IGuidGenerator guidGenerator,
        IClock clock,
        IRepository<PayflowPayment, Guid> payflowPayments,
        IPayflowGateway payflowGateway,
        IOptions<PayflowOptions> payflowOptions,
        ILogger<BillApprovalModel> logger)
    {
        _themeProvider = themeProvider;
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _studentLookups = studentLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _scheduleLookups = scheduleLookups.ToArray();
        _billingLookups = billingLookups.ToArray();
        _aidLookups = aidLookups.ToArray();
        _paymentPlanLookups = paymentPlanLookups.ToArray();
        _documentTrackingServices = documentTrackingServices.ToArray();
        _paymentPostingServices = paymentPostingServices.ToArray();
        _configuration = configuration;
        _agreementTemplates = agreementTemplates;
        _paymentPlanPolicies = paymentPlanPolicies;
        _termConfigurations = termConfigurations;
        _billApprovals = billApprovals;
        _artifacts = artifacts;
        _pdfs = pdfs;
        _pdfGenerator = pdfGenerator;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _payflowPayments = payflowPayments;
        _payflowGateway = payflowGateway;
        _payflowOptions = payflowOptions.Value;
        _logger = logger;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public string StudentName { get; private set; } = "Student";
    public string StudentId { get; private set; } = "Unavailable";
    public string TermName { get; private set; } = "Current term";
    public string? TermCode { get; private set; }
    public string? ExternalTermId { get; private set; }
    public bool IsUnavailable { get; private set; }
    public IReadOnlyList<BillApprovalTermConfiguration> AvailableTerms { get; private set; } = [];
    public IReadOnlyList<StudentCourseScheduleItem> Courses { get; private set; } = [];
    public IReadOnlyList<StudentBillingTransaction> Charges { get; private set; } = [];
    public IReadOnlyList<StudentFinancialAidAward> Aid { get; private set; } = [];
    public StudentPaymentPlan? PaymentPlan { get; private set; }
    public AgreementTemplate? Agreement { get; private set; }
    public bool IsAlreadyAccepted { get; private set; }
    public bool PdfAvailable { get; private set; }
    public bool DocumentTrackingCompleted { get; private set; }
    public string? PdfFileName { get; private set; }
    [BindProperty] public string? PaymentChoiceInput { get; set; }
    [BindProperty] public string PaymentPurposeInput { get; set; } = "PayNow";
    [BindProperty] public bool AgreementAcceptedInput { get; set; }
    [BindProperty, Required, CreditCard, Display(Name = "Card number")]
    public string PaymentCardNumber { get; set; } = string.Empty;
    [BindProperty, Range(1, 12), Display(Name = "Expiration month")]
    public int PaymentExpirationMonth { get; set; }
    [BindProperty, Range(2026, 2100), Display(Name = "Expiration year")]
    public int PaymentExpirationYear { get; set; }
    [BindProperty, Required, RegularExpression(@"^\d{3,4}$"), Display(Name = "Security code")]
    public string PaymentSecurityCode { get; set; } = string.Empty;
    [BindProperty, StringLength(20), Display(Name = "Billing ZIP/postal code")]
    public string? PaymentPostalCode { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    public string InitialStep { get; private set; } = "review";
    public string InitialPaymentChoice { get; private set; } = "PayNow";
    public bool IsPayflowTestMode => _payflowOptions.TestMode;
    public decimal DeferredDueToday => PaymentPlan?.Installments.FirstOrDefault()?.Amount ?? 0m;
    public decimal TotalCredits => Courses.Sum(x => x.Credits);
    public decimal ChargesTotal => Charges.Where(x => !x.IsVoided).Sum(x => x.Debit);
    public decimal CreditsTotal => Charges.Where(x => !x.IsVoided).Sum(x => x.Credit);
    public decimal PendingAidTotal => Aid.Where(IsEligibleAid).Sum(x => x.Amount);
    public decimal AccountBalance { get; private set; }
    public decimal RemainingBalance => AccountBalance - PendingAidTotal;

    public static string Currency(decimal value) => value.ToString("$#,##0.00;($#,##0.00);$0.00");
    public static string Credits(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public async Task OnGetAsync([FromQuery] string? term = null, [FromQuery] string? step = null,
        [FromQuery] string? choice = null)
    {
        InitialStep = step is "payment" or "agreement" or "finish" ? step : "review";
        InitialPaymentChoice = choice == "Deferred" || PaymentPurposeInput == "Deferred" ? "Deferred" : "PayNow";
        if (PaymentExpirationMonth == 0) PaymentExpirationMonth = DateTime.UtcNow.Month;
        if (PaymentExpirationYear == 0) PaymentExpirationYear = DateTime.UtcNow.Year;
        Theme = _themeProvider.Get(CurrentTenant.Name);
        if (CurrentUser.Id is null) return;

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) { IsUnavailable = true; return; }

        try
        {
            var liveStudentLookup = _studentLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (!_currentStudentView.IsImpersonating && liveStudentLookup is not null && !string.IsNullOrWhiteSpace(CurrentUser.Email))
            {
                var refreshed = await liveStudentLookup.FindByEmailAsync(CurrentUser.Email, HttpContext.RequestAborted);
                if (refreshed.Student is not null)
                {
                    profile.Update(refreshed.Student);
                    await _profiles.UpdateAsync(profile, autoSave: true, cancellationToken: HttpContext.RequestAborted);
                }
            }

            StudentName = profile.DisplayName;
            StudentId = profile.StudentId;
            var termLookup = _termLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var scheduleLookup = _scheduleLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var billingLookup = _billingLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            var aidLookup = _aidLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (termLookup is null || scheduleLookup is null || billingLookup is null || aidLookup is null)
            {
                IsUnavailable = true;
                return;
            }

            var currentTermTask = termLookup.GetCurrentTermAsync(HttpContext.RequestAborted);
            var scheduleTask = scheduleLookup.GetScheduleAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            var billingTask = billingLookup.GetTransactionsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            var aidTask = aidLookup.GetAwardsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            await Task.WhenAll(currentTermTask, scheduleTask, billingTask, aidTask);

            var currentTerm = await currentTermTask;
            var configuredTerms = await _termConfigurations.GetListAsync();
            AvailableTerms = configuredTerms.Where(x => x.IsOpen(_clock.Now)).OrderByDescending(x => x.TermCode).ToArray();
            if (configuredTerms.Count > 0)
            {
                TermCode = string.IsNullOrWhiteSpace(term)
                    ? AvailableTerms.FirstOrDefault(x => x.TermCode == currentTerm?.TermCode)?.TermCode ?? AvailableTerms.FirstOrDefault()?.TermCode
                    : term;
                if (TermCode is null || AvailableTerms.All(x => x.TermCode != TermCode))
                {
                    IsUnavailable = true;
                    return;
                }
            }
            else TermCode = string.IsNullOrWhiteSpace(term) ? currentTerm?.TermCode : term;
            var termConfiguration = configuredTerms.FirstOrDefault(x => x.TermCode == TermCode);
            var allCourses = await scheduleTask;
            Courses = allCourses.Where(x => x.TermCode == TermCode)
                .OrderBy(x => x.Department).ThenBy(x => x.CourseNumber).ToArray();
            var courseTerm = Courses.FirstOrDefault();
            TermName = courseTerm?.TermName ?? currentTerm?.DisplayName ?? "Current term";
            ExternalTermId = courseTerm?.ExternalTermId;

            var allTransactions = await billingTask;
            Charges = allTransactions.Where(x => x.TermCode == TermCode)
                .OrderBy(x => x.TransactionDate).ThenBy(x => x.ExternalTransactionId).ToArray();
            AccountBalance = allTransactions
                .Where(x => !x.IsVoided && string.CompareOrdinal(x.TermCode, TermCode) <= 0)
                .Sum(x => x.BalanceChange);
            Aid = (await aidTask).Where(x => x.TermCode == TermCode)
                .OrderByDescending(x => x.Amount).ToArray();
            var paymentPlanLookup = _paymentPlanLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (RemainingBalance > 0 && paymentPlanLookup is not null && !string.IsNullOrWhiteSpace(ExternalTermId))
            {
                var context = await paymentPlanLookup.GetPaymentPlanContextAsync(
                    profile.ExternalStudentId, ExternalTermId, HttpContext.RequestAborted);
                if (context is not null)
                {
                    var policy = await GetCurrentPaymentPlanPolicyAsync(termConfiguration?.PaymentPlanPolicyId);
                    PaymentPlan = StudentPaymentPlanCalculator.Calculate(
                        AccountBalance, PendingAidTotal, TermName, context, policy);
                }
            }

            Agreement = await FindCurrentAgreementAsync(termConfiguration?.AgreementTemplateId);
            if (!string.IsNullOrWhiteSpace(ExternalTermId))
            {
                IsAlreadyAccepted = await _billApprovals.AnyAsync(x =>
                    x.UserId == profile.UserId && x.ExternalTermId == ExternalTermId && x.AcceptedAt != null);
                if (IsAlreadyAccepted)
                {
                    var accepted = await _billApprovals.FindAsync(x =>
                        x.UserId == profile.UserId && x.ExternalTermId == ExternalTermId && x.AcceptedAt != null);
                    if (accepted is not null)
                    {
                        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == accepted.Id);
                        PdfAvailable = artifact?.PdfStatus == BillArtifactOperationStatus.Completed &&
                                       !string.IsNullOrWhiteSpace(artifact.PdfBlobName);
                        DocumentTrackingCompleted = artifact?.DocumentUploadStatus == BillArtifactOperationStatus.Completed;
                        PdfFileName = artifact?.PdfFileName;
                    }
                }
            }
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            IsUnavailable = true;
            _logger.LogWarning(exception, "Unable to prepare bill approval review for the current student.");
        }
    }

    public async Task<IActionResult> OnPostPaymentAsync([FromQuery] string? term = null)
    {
        InitialStep = "payment";
        await OnGetAsync(term, InitialStep, PaymentPurposeInput);
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (_currentStudentView.IsImpersonating || CurrentUser.Id is null || profile is null)
            return Unauthorized();

        if (IsUnavailable || string.IsNullOrWhiteSpace(ExternalTermId))
            ModelState.AddModelError(string.Empty, "Your account information could not be loaded. No payment was made.");
        if (!_payflowOptions.IsConfigured)
            ModelState.AddModelError(string.Empty, "Online payments are not configured yet.");
        if (RemainingBalance <= 0)
            ModelState.AddModelError(string.Empty, "There is no remaining balance to pay.");
        if (PaymentPurposeInput == "Deferred" && PaymentPlan is null)
            ModelState.AddModelError(string.Empty, "The deferred payment plan could not be calculated. No payment was made.");
        if (!int.TryParse(ExternalTermId, out var termCalendarId))
            ModelState.AddModelError(string.Empty, "The selected academic term could not be verified. No payment was made.");
        var now = DateTime.UtcNow;
        if (PaymentExpirationYear < now.Year || PaymentExpirationYear == now.Year && PaymentExpirationMonth < now.Month)
            ModelState.AddModelError(nameof(PaymentExpirationMonth), "Enter a future expiration date.");
        if (!ModelState.IsValid)
        {
            ClearPaymentFields();
            return Page();
        }

        var isDeferredPayment = PaymentPurposeInput == "Deferred";
        var amount = decimal.Round(isDeferredPayment ? DeferredDueToday : Math.Max(RemainingBalance, 0), 2);
        if (amount <= 0)
        {
            ModelState.AddModelError(string.Empty, "No payment is due today for this selection.");
            ClearPaymentFields();
            return Page();
        }
        var id = _guidGenerator.Create();
        var payment = new PayflowPayment(id, CurrentTenant.Id, CurrentUser.Id.Value, profile.Id,
            profile.ExternalStudentId, amount, _payflowOptions.Currency, Guid.NewGuid().ToString("N"),
            _payflowOptions.TestMode);
        await _payflowPayments.InsertAsync(payment, autoSave: true);
        try
        {
            var number = Regex.Replace(PaymentCardNumber, @"[\s-]", string.Empty);
            var response = await _payflowGateway.SaleAsync(amount, $"CF{id:N}"[..20],
                new PayflowCard(number, PaymentExpirationMonth, PaymentExpirationYear,
                    PaymentSecurityCode, PaymentPostalCode), HttpContext.RequestAborted);
            payment.Complete(response.IsDirectSaleApproved, response.Result, response.Message, response.Reference);
            await _payflowPayments.UpdateAsync(payment, autoSave: true);
            _logger.LogInformation(
                "Bill approval Payflow sale completed for payment {PaymentId}. Result={GatewayResult}, Reference={GatewayReference}",
                id, response.Result, response.Reference);
            if (!response.IsDirectSaleApproved)
            {
                ModelState.AddModelError(string.Empty,
                    "The payment was not approved. Please review the card details or try another payment method.");
                ClearPaymentFields();
                return Page();
            }

            var accountUpdatePending = false;
            try
            {
                var postingService = _paymentPostingServices.Single(x => x.Provider == profile.Provider);
                var posting = await postingService.PostAsync(profile.ExternalStudentId, termCalendarId, amount,
                    response.Reference ?? string.Empty, payment.IsTest, DateTime.UtcNow,
                    HttpContext.RequestAborted);
                payment.MarkElementsPosted(posting.BatchMasterId, posting.BillingBatchId);
                await _payflowPayments.UpdateAsync(payment, autoSave: true);
            }
            catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                accountUpdatePending = true;
                payment.MarkElementsPostingFailed(exception.Message);
                await _payflowPayments.UpdateAsync(payment, autoSave: true);
                _logger.LogError(exception,
                    "Bill approval payment {PaymentId} was approved but could not be posted to Elements", id);
            }

            StatusMessage = accountUpdatePending
                ? $"Your payment of {Currency(amount)} was received. Your student account is still updating, but you can continue reviewing the agreement."
                : isDeferredPayment
                    ? $"Your payment-plan amount of {Currency(amount)} due today was received. You can now review and record your agreement."
                    : $"Your payment of {Currency(amount)} was received. You can now review and record your agreement.";
            ClearPaymentFields();
            return RedirectToPage(new { term, step = "agreement", choice = isDeferredPayment ? "Deferred" : "PayNow" });
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            payment.Fail(null, "Gateway unavailable");
            await _payflowPayments.UpdateAsync(payment, autoSave: true);
            _logger.LogWarning(exception, "Unable to process bill approval Payflow payment {PaymentId}", id);
            ModelState.AddModelError(string.Empty, "PayPal is temporarily unavailable. No payment was made.");
            ClearPaymentFields();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync([FromQuery] string? term = null)
    {
        await OnGetAsync(term);
        if (IsUnavailable || CurrentUser.Id is null || string.IsNullOrWhiteSpace(ExternalTermId) || Agreement is null)
        {
            StatusMessage = "Your agreement could not be saved. Please reload the page and try again.";
            return RedirectToPage(new { term });
        }

        if (!AgreementAcceptedInput)
        {
            ModelState.AddModelError(string.Empty, "You must review and accept the agreement before continuing.");
            return Page();
        }

        var paymentChoice = RemainingBalance <= 0
            ? BillPaymentChoice.NoBalanceDue
            : PaymentChoiceInput == "Deferred"
                ? BillPaymentChoice.DeferredPaymentPlan
                : BillPaymentChoice.PayNow;
        if (RemainingBalance > 0 && paymentChoice == BillPaymentChoice.PayNow)
        {
            ModelState.AddModelError(string.Empty, "The remaining balance must be paid before the bill can be approved.");
            return Page();
        }

        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) return Unauthorized();
        var approval = await _billApprovals.FindAsync(x =>
            x.UserId == CurrentUser.Id.Value && x.ExternalTermId == ExternalTermId);
        if (approval?.AcceptedAt is not null)
        {
            StatusMessage = "Your bill agreement was already recorded.";
            return RedirectToPage(new { term });
        }

        var isNew = approval is null;
        approval ??= new BillApproval(_guidGenerator.Create(), CurrentTenant.Id, CurrentUser.Id.Value, profile.Id,
            profile.ExternalStudentId, profile.StudentId, ExternalTermId, TermCode ?? "", TermName);
        var scheduleJson = JsonSerializer.Serialize(paymentChoice == BillPaymentChoice.DeferredPaymentPlan
            ? PaymentPlan?.Installments ?? []
            : []);
        var reviewSnapshotJson = JsonSerializer.Serialize(new BillApprovalReviewSnapshot(
            StudentName,
            TotalCredits,
            Courses.Select(x => new BillApprovalCourseSnapshot(
                $"{x.Department} {x.CourseNumber}-{x.Section}", x.CourseName, x.Credits, x.Instructor)).ToArray(),
            Charges.Where(x => !x.IsVoided).Select(x => new BillApprovalTransactionSnapshot(
                x.TransactionDate, x.Description, x.BalanceChange, x.IsPending)).ToArray(),
            Aid.Where(IsEligibleAid).Select(x => new BillApprovalAidSnapshot(
                x.Description, x.Amount, x.AwardStatus)).ToArray()));
        approval.UpdateDraft(paymentChoice, ChargesTotal, CreditsTotal, PendingAidTotal, RemainingBalance,
            paymentChoice == BillPaymentChoice.DeferredPaymentPlan ? 100m : 0m, scheduleJson, reviewSnapshotJson);
        approval.Accept(Agreement.Id, Agreement.Version, Agreement.ContentHtml, _clock.Now,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

        if (isNew)
            await _billApprovals.InsertAsync(approval, autoSave: true);
        else
            await _billApprovals.UpdateAsync(approval, autoSave: true);
        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == approval.Id);
        if (artifact is null)
        {
            artifact = new BillApprovalArtifact(_guidGenerator.Create(), CurrentTenant.Id, approval.Id);
            await _artifacts.InsertAsync(artifact, autoSave: true);
        }

        byte[] pdfBytes;
        try
        {
            pdfBytes = await GeneratePdfAsync(approval, artifact);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            artifact.MarkPdfFailed(exception.Message, _clock.Now);
            await _artifacts.UpdateAsync(artifact, autoSave: true);
            _logger.LogError(exception, "Agreement {BillApprovalId} was recorded, but its PDF could not be generated.", approval.Id);
            StatusMessage = "Your agreement was securely recorded. The approved document is still being prepared.";
            return RedirectToPage(new { term });
        }
        try
        {
            await UploadToDocumentTrackingAsync(approval, artifact, pdfBytes);
            StatusMessage = "Your agreement was recorded, and your approved bill is available in CampusFlow and Elements Document Tracking.";
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            artifact.MarkDocumentUploadFailed(exception.Message, _clock.Now);
            await _artifacts.UpdateAsync(artifact, autoSave: true);
            _logger.LogError(exception, "Approved bill {BillApprovalId} could not be delivered to Elements Document Tracking.", approval.Id);
            StatusMessage = "Your agreement and PDF are secure. Delivery to Elements Document Tracking is still pending.";
        }
        return RedirectToPage(new { term });
    }

    public async Task<IActionResult> OnPostGeneratePdfAsync([FromQuery] string term)
    {
        if (CurrentUser.Id is null || string.IsNullOrWhiteSpace(term)) return NotFound();
        var approval = await _billApprovals.FindAsync(x =>
            x.UserId == CurrentUser.Id.Value && x.TermCode == term && x.AcceptedAt != null);
        if (approval is null) return NotFound();
        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == approval.Id);
        if (artifact is null)
        {
            artifact = new BillApprovalArtifact(_guidGenerator.Create(), CurrentTenant.Id, approval.Id);
            await _artifacts.InsertAsync(artifact, autoSave: true);
        }
        byte[] pdfBytes;
        try
        {
            pdfBytes = await GeneratePdfAsync(approval, artifact);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            artifact.MarkPdfFailed(exception.Message, _clock.Now);
            await _artifacts.UpdateAsync(artifact, autoSave: true);
            _logger.LogError(exception, "Approved bill {BillApprovalId} could not be generated.", approval.Id);
            StatusMessage = "Your agreement is secure, but the approved document could not be prepared yet.";
            return RedirectToPage(new { term });
        }
        try
        {
            await UploadToDocumentTrackingAsync(approval, artifact, pdfBytes);
            StatusMessage = "Your approved bill is ready and has been added to Elements Document Tracking.";
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            artifact.MarkDocumentUploadFailed(exception.Message, _clock.Now);
            await _artifacts.UpdateAsync(artifact, autoSave: true);
            _logger.LogError(exception, "Approved bill {BillApprovalId} could not be delivered to Elements Document Tracking.", approval.Id);
            StatusMessage = "Your PDF is ready, but delivery to Elements Document Tracking is still pending.";
        }
        return RedirectToPage(new { term });
    }

    public async Task<IActionResult> OnGetPdfAsync([FromQuery] string term)
    {
        if (CurrentUser.Id is null || string.IsNullOrWhiteSpace(term)) return NotFound();
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) return NotFound();
        var approval = await _billApprovals.FindAsync(x =>
            x.UserId == profile.UserId && x.TermCode == term && x.AcceptedAt != null);
        if (approval is null) return NotFound();
        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == approval.Id);
        if (artifact?.PdfStatus != BillArtifactOperationStatus.Completed ||
            string.IsNullOrWhiteSpace(artifact.PdfBlobName) || string.IsNullOrWhiteSpace(artifact.PdfFileName))
            return NotFound();
        var bytes = await _pdfs.GetAllBytesAsync(artifact.PdfBlobName, HttpContext.RequestAborted);
        return File(bytes, "application/pdf", artifact.PdfFileName);
    }

    private async Task<byte[]> GeneratePdfAsync(BillApproval approval, BillApprovalArtifact artifact)
    {
        var bytes = _pdfGenerator.Generate(approval);
        var safeStudentId = string.Concat(approval.StudentId.Where(char.IsLetterOrDigit));
        var safeTermCode = string.Concat(approval.TermCode.Where(char.IsLetterOrDigit));
        var fileName = $"Approved-Bill-{safeStudentId}-{safeTermCode}.pdf";
        var blobName = $"{approval.Id:N}/{fileName}";
        await _pdfs.SaveAsync(blobName, bytes, overrideExisting: true, cancellationToken: HttpContext.RequestAborted);
        artifact.MarkPdfCompleted(fileName, blobName, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), _clock.Now);
        await _artifacts.UpdateAsync(artifact, autoSave: true);
        return bytes;
    }

    private async Task UploadToDocumentTrackingAsync(
        BillApproval approval, BillApprovalArtifact artifact, byte[] pdfBytes)
    {
        if (artifact.DocumentUploadStatus == BillArtifactOperationStatus.Completed) return;
        var service = _documentTrackingServices.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        var documentTrackingId = artifact.ElementsDocumentTrackingId;
        if (string.IsNullOrWhiteSpace(documentTrackingId))
        {
            documentTrackingId = await service.CreateApprovedBillAsync(new StudentDocumentTrackingRequest(
                approval.ExternalStudentId, approval.TermName, approval.Id, approval.AcceptedAt!.Value),
                HttpContext.RequestAborted);
            artifact.MarkDocumentCreated(documentTrackingId, _clock.Now);
            await _artifacts.UpdateAsync(artifact, autoSave: true);
        }
        if (!await service.HasImageAsync(documentTrackingId, HttpContext.RequestAborted))
            await service.UploadImageAsync(documentTrackingId, artifact.PdfFileName!, pdfBytes, HttpContext.RequestAborted);
        artifact.MarkDocumentUploadCompleted(_clock.Now);
        await _artifacts.UpdateAsync(artifact, autoSave: true);
    }

    private async Task<AgreementTemplate?> FindCurrentAgreementAsync(Guid? configuredAgreementId = null)
    {
        if (configuredAgreementId.HasValue)
            return await _agreementTemplates.GetAsync(configuredAgreementId.Value);
        var now = _clock.Now;
        var query = await _agreementTemplates.GetQueryableAsync();
        return query.Where(x => x.IsPublished && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo >= now))
            .OrderByDescending(x => x.Version).FirstOrDefault();
    }

    private async Task<StudentPaymentPlanPolicy> GetCurrentPaymentPlanPolicyAsync(Guid? configuredPolicyId = null)
    {
        var now = _clock.Now;
        var query = await _paymentPlanPolicies.GetQueryableAsync();
        var stored = configuredPolicyId.HasValue
            ? query.FirstOrDefault(x => x.Id == configuredPolicyId.Value)
            : query.Where(x => x.IsPublished && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo >= now))
                .OrderByDescending(x => x.Version).FirstOrDefault();
        if (stored is not null)
        {
            return new StudentPaymentPlanPolicy(
                stored.EnrollmentFee, stored.PartTimeBalanceDivisor, stored.ResidentialMinimumPayment,
                stored.StandardMinimumPayment,
                JsonSerializer.Deserialize<string[]>(stored.ResidentialAttendanceTypesJson) ?? [],
                JsonSerializer.Deserialize<string[]>(stored.FallDueDatesJson) ?? [],
                JsonSerializer.Deserialize<string[]>(stored.SpringDueDatesJson) ?? [],
                JsonSerializer.Deserialize<string[]>(stored.SummerDueDatesJson) ?? []);
        }

        var section = _configuration.GetSection("BillApproval:PaymentPlan");
        return new StudentPaymentPlanPolicy(
            section.GetValue("EnrollmentFee", 100m), section.GetValue("PartTimeBalanceDivisor", 3m),
            section.GetValue("ResidentialMinimumPayment", 3500m), section.GetValue("StandardMinimumPayment", 1500m),
            section.GetSection("ResidentialAttendanceTypes").Get<string[]>() ?? [],
            section.GetSection("FallDueDates").Get<string[]>() ?? [],
            section.GetSection("SpringDueDates").Get<string[]>() ?? [],
            section.GetSection("SummerDueDates").Get<string[]>() ?? []);
    }

    private void ClearPaymentFields()
    {
        var cardErrors = ModelState.TryGetValue(nameof(PaymentCardNumber), out var cardState)
            ? cardState.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            : [];
        var securityCodeErrors = ModelState.TryGetValue(nameof(PaymentSecurityCode), out var securityCodeState)
            ? securityCodeState.Errors.Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            : [];
        PaymentCardNumber = string.Empty;
        PaymentSecurityCode = string.Empty;
        ModelState.Remove(nameof(PaymentCardNumber));
        ModelState.Remove(nameof(PaymentSecurityCode));
        foreach (var error in cardErrors) ModelState.AddModelError(nameof(PaymentCardNumber), error);
        foreach (var error in securityCodeErrors) ModelState.AddModelError(nameof(PaymentSecurityCode), error);
    }

    private static bool IsEligibleAid(StudentFinancialAidAward award) =>
        award.Amount > 0 && !award.IsSentToBilling && award.StudentAccepted != 0 &&
        !award.AwardStatus.Contains("Cancel", StringComparison.OrdinalIgnoreCase);
}
