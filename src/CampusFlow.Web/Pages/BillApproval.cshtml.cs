using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Payments;
using CampusFlow.Web.Bookstores;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.BlobStoring;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.BackgroundJobs;

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
    private readonly IReadOnlyCollection<IStudentInformationSystemFinancialAidDecisionService> _aidDecisions;
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
    private readonly ILegacyBillApprovalLookup _legacyApprovals;
    private readonly IBillApprovalCompletionLookup _completion;
    private readonly IBackgroundJobManager _jobs;
    private readonly IStudentTermSelection _selectedTerm;
    private readonly IReadOnlyCollection<IStudentInformationSystemBillingRecalculationService> _recalculations;
    private readonly CampusFlow.Housing.IMealPlanAppService _mealPlans;

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
        ILogger<BillApprovalModel> logger,
        ILegacyBillApprovalLookup legacyApprovals, CampusFlow.Housing.IMealPlanAppService mealPlans, IBillApprovalCompletionLookup completion, IStudentTermSelection selectedTerm, IEnumerable<IStudentInformationSystemBillingRecalculationService> recalculations, IBackgroundJobManager jobs, IEnumerable<IStudentInformationSystemFinancialAidDecisionService>? aidDecisions = null)
    {
        _themeProvider = themeProvider;
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _studentLookups = studentLookups.ToArray();
        _termLookups = termLookups.ToArray();
        _scheduleLookups = scheduleLookups.ToArray();
        _billingLookups = billingLookups.ToArray();
        _aidLookups = aidLookups.ToArray();
        _aidDecisions = aidDecisions?.ToArray() ?? [];
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
        _legacyApprovals = legacyApprovals;
        _mealPlans = mealPlans;
        _completion = completion;
        _selectedTerm = selectedTerm;
        _recalculations = recalculations.ToArray();
        _jobs = jobs;
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
    public string? UnavailableReason { get; private set; }
    public IReadOnlyList<BillApprovalTermConfiguration> AvailableTerms { get; private set; } = [];
    public IReadOnlyList<StudentCourseScheduleItem> Courses { get; private set; } = [];
    public string? BookstoreUrl { get; private set; }
    public IReadOnlyList<StudentBillingTransaction> Charges { get; private set; } = [];
    public IReadOnlyList<StudentFinancialAidAward> Aid { get; private set; } = [];
    public StudentPaymentPlan? PaymentPlan { get; private set; }
    public AgreementTemplate? Agreement { get; private set; }
    public bool IsAlreadyAccepted { get; private set; }
    public LegacyBillApproval? LegacyApproval { get; private set; }
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
    public decimal ConvenienceFeePercent => _payflowOptions.ConvenienceFeeRate * 100m;
    [BindProperty] public string? ReviewVersion { get; set; }
    public string CurrentReviewVersion { get; private set; } = string.Empty;
    public decimal PaymentPlanFee => PaymentPlan?.PaymentPlanFee ?? 0m;
    public decimal DeferredBalance => PaymentPlan?.RemainingBalance ?? RemainingBalance;
    public decimal DeferredDueToday => PaymentPlan?.Installments.FirstOrDefault()?.Amount ?? 0m;
    public decimal ConvenienceFee(decimal amount) => _payflowOptions.CalculateConvenienceFee(amount);
    public decimal TotalCardCharge(decimal amount) => amount + ConvenienceFee(amount);
    public decimal TotalCredits => Courses.Sum(x => x.Credits);
    public decimal ChargesTotal => Charges.Where(x => !x.IsVoided).Sum(x => x.Debit);
    public decimal CreditsTotal => Charges.Where(x => !x.IsVoided).Sum(x => x.Credit);
    public decimal PendingAidTotal => Aid.Where(IsEligibleAid).Sum(x => x.Amount);
    public decimal AccountBalance { get; private set; }
    public decimal RemainingBalance => AccountBalance - PendingAidTotal;

    public static string Currency(decimal value) => CampusFlow.Web.Formatting.UsdCurrency.Format(value);
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

            var currentTermTask = _selectedTerm.ResolveAsync(profile, termLookup, cancellationToken: HttpContext.RequestAborted);
            var scheduleTask = scheduleLookup.GetScheduleAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            var aidTask = aidLookup.GetAwardsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            await Task.WhenAll(currentTermTask, scheduleTask, aidTask);

            var currentTerm = await currentTermTask;
            var configuredTerms = await _termConfigurations.GetListAsync();
            var approvedTerms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var configured in configuredTerms)
                if (await _completion.IsApprovedAsync(profile, configured.ExternalTermId, HttpContext.RequestAborted))
                    approvedTerms.Add(configured.ExternalTermId);
            AvailableTerms = configuredTerms.Where(x => x.IsOpenForStudent(_clock.Now, profile.StudentId) || approvedTerms.Contains(x.ExternalTermId))
                .OrderByDescending(x => x.TermCode).ToArray();
            TermCode = string.IsNullOrWhiteSpace(term)
                ? AvailableTerms.FirstOrDefault(x => x.TermCode == currentTerm?.TermCode)?.TermCode ?? AvailableTerms.FirstOrDefault()?.TermCode
                : term;
            if (TermCode is null || AvailableTerms.All(x => x.TermCode != TermCode))
            {
                IsUnavailable = true;
                return;
            }
            var termConfiguration = configuredTerms.FirstOrDefault(x => x.TermCode == TermCode);
            var allCourses = await scheduleTask;
            Courses = allCourses.Where(x => x.TermCode == TermCode)
                .OrderBy(x => x.Department).ThenBy(x => x.CourseNumber).ToArray();
            var courseTerm = Courses.FirstOrDefault();
            TermName = courseTerm?.TermName ?? termConfiguration?.TermName ?? currentTerm?.DisplayName ?? "Current term";
            BookstoreUrl = CampusStoreLinks.ForCourses(_configuration, CurrentTenant.Name, TermName, Courses);
            ExternalTermId = courseTerm?.ExternalTermId;
            ExternalTermId ??= termConfiguration?.ExternalTermId;

            if (ExternalTermId is not null)
                await _selectedTerm.ResolveAsync(profile, termLookup, ExternalTermId, cancellationToken: HttpContext.RequestAborted);
            var alreadyApproved = ExternalTermId is not null && approvedTerms.Contains(ExternalTermId);
            var housing = !alreadyApproved && profile.Provider == StudentInformationSystemProvider.ThesisElements
                ? await _mealPlans.GetAsync(ExternalTermId) : null;
            if (housing is { HousingRequired: true, HasHousingAssignment: false })
            {
                IsUnavailable = true;
                UnavailableReason = "A housing assignment is required before bill approval. Contact Student Services.";
                return;
            }

            if (!alreadyApproved)
            {
                var previousBalance = await billingLookup.GetPreviousBalanceAsync(profile.ExternalStudentId, ExternalTermId!, HttpContext.RequestAborted);
                if (BillApprovalAccessRules.HasBlockingPreviousBalance(previousBalance))
                {
                    IsUnavailable = true;
                    UnavailableReason = "Your previous balance needs attention before bill approval. You may still select courses and a meal plan. Contact Student Billing.";
                    return;
                }
            }

            if (!alreadyApproved && !_currentStudentView.IsImpersonating)
            {
                var recalculation = _recalculations.SingleOrDefault(x => x.Provider == profile.Provider)
                    ?? throw new InvalidOperationException("The billing recalculation provider is unavailable.");
                await recalculation.RecalculateAsync(profile.ExternalStudentId, ExternalTermId!, Guid.NewGuid(), HttpContext.RequestAborted);
            }
            // Read billing only after Elements has confirmed completion; never reuse the pre-recalculation ledger.
            var allTransactions = await billingLookup.GetTransactionsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
            Charges = allTransactions.Where(x => x.TermCode == TermCode)
                .OrderBy(x => x.TransactionDate).ThenBy(x => x.ExternalTransactionId).ToArray();
            AccountBalance = allTransactions
                .Where(x => !x.IsVoided && string.CompareOrdinal(x.TermCode, TermCode) <= 0)
                .Sum(x => x.BalanceChange);
            var termAid = (await aidTask).Where(x => x.ExternalTermId == ExternalTermId).ToArray();
            var decisionReader = _aidDecisions.SingleOrDefault(x => x.Provider == profile.Provider);
            if (decisionReader is not null)
            {
                var decisions = await decisionReader.GetDecisionsAsync(profile.ExternalStudentId, HttpContext.RequestAborted);
                if (termAid.Any(x => !decisions.ContainsKey(x.ExternalAwardId)))
                    throw new InvalidOperationException("Financial aid decisions could not be verified.");
                termAid = termAid.Select(x => x with { StudentAccepted = decisions[x.ExternalAwardId] }).ToArray();
            }
            Aid = termAid.Where(IsEligibleAid).OrderByDescending(x => x.Amount).ToArray();
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
            CurrentReviewVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { profile.Id, ExternalTermId, AccountBalance,
                Courses = Courses.Select(x => new { x.Department, x.CourseNumber, x.Section, x.CourseName, x.Credits, x.Instructor }),
                Charges = Charges.Where(x => !x.IsVoided).OrderBy(x => x.Description).ThenBy(x => x.BalanceChange)
                    .Select(x => new { x.Description, Date = x.TransactionDate.Date, x.Debit, x.Credit, x.BalanceChange, x.IsPending }),
                Aid = Aid.Where(IsEligibleAid).OrderBy(x => x.Description).ThenBy(x => x.Amount).Select(x => new { x.Description, x.Amount, x.AwardStatus }),
                PaymentPlan, AgreementId = Agreement?.Id, AgreementVersion = Agreement?.Version, AgreementHtml = Agreement?.ContentHtml }))));
            if (!string.IsNullOrWhiteSpace(ExternalTermId))
            {
                IsAlreadyAccepted = await _billApprovals.AnyAsync(x =>
                    x.StudentProfileId == profile.Id && x.ExternalTermId == ExternalTermId && x.AcceptedAt != null);
                if (IsAlreadyAccepted)
                {
                    var accepted = await _billApprovals.FindAsync(x =>
                        x.StudentProfileId == profile.Id && x.ExternalTermId == ExternalTermId && x.AcceptedAt != null);
                    if (accepted is not null)
                    {
                        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == accepted.Id);
                        PdfAvailable = artifact?.PdfStatus == BillArtifactOperationStatus.Completed &&
                                       !string.IsNullOrWhiteSpace(artifact.PdfBlobName);
                        DocumentTrackingCompleted = artifact?.DocumentUploadStatus == BillArtifactOperationStatus.Completed;
                        PdfFileName = artifact?.PdfFileName;
                    }
                }
                if (!IsAlreadyAccepted && profile.Provider == StudentInformationSystemProvider.ThesisElements &&
                    !await _billApprovals.AnyAsync(x => x.StudentProfileId == profile.Id && x.ExternalTermId == ExternalTermId))
                {
                    LegacyApproval = await _legacyApprovals.FindAsync(profile.ExternalStudentId, ExternalTermId, HttpContext.RequestAborted);
                    IsAlreadyAccepted = LegacyApproval?.CountsAsApproved == true;
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
        if (IsAlreadyAccepted) return RedirectToPage(new { term });
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (_currentStudentView.IsImpersonating || CurrentUser.Id is null || profile is null)
            return Unauthorized();

        if (IsUnavailable || string.IsNullOrWhiteSpace(ExternalTermId))
            ModelState.AddModelError(string.Empty, "Your account information could not be loaded. No payment was made.");
        if (string.IsNullOrEmpty(ReviewVersion) || ReviewVersion != CurrentReviewVersion)
            ModelState.AddModelError(string.Empty, "Your bill changed or needs a fresh review. Review the updated amounts before submitting payment.");
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
        var convenienceFee = _payflowOptions.CalculateConvenienceFee(amount);
        var totalCharge = amount + convenienceFee;
        var payment = new PayflowPayment(id, CurrentTenant.Id, CurrentUser.Id.Value, profile.Id,
            profile.ExternalStudentId, amount, convenienceFee, _payflowOptions.Currency, Guid.NewGuid().ToString("N"),
            _payflowOptions.TestMode);
        payment.SetElementsTerm(termCalendarId);
        await _payflowPayments.InsertAsync(payment, autoSave: true);
        try
        {
            var number = Regex.Replace(PaymentCardNumber, @"[\s-]", string.Empty);
            var response = await _payflowGateway.SaleAsync(totalCharge, $"CF{id:N}"[..20],
                new PayflowCard(number, PaymentExpirationMonth, PaymentExpirationYear,
                    PaymentSecurityCode, PaymentPostalCode),
                new PayflowCustomer(profile.StudentId, profile.FirstName, profile.LastName),
                HttpContext.RequestAborted);
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
                    convenienceFee, response.Reference ?? string.Empty, payment.IsTest, payment.CreationTime,
                    HttpContext.RequestAborted);
                payment.MarkElementsPosted(posting.BatchMasterId, posting.BillingBatchId,
                    posting.ConvenienceFeeBillingBatchId);
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
                ? $"Your card charge of {Currency(totalCharge)} ({Currency(amount)} payment plus {Currency(convenienceFee)} convenience fee) was received. Your student account is still updating, but you can continue reviewing the agreement."
                : isDeferredPayment
                    ? $"Your payment-plan card charge of {Currency(totalCharge)} ({Currency(amount)} payment plus {Currency(convenienceFee)} convenience fee) was received. You can now review and record your agreement."
                    : $"Your card charge of {Currency(totalCharge)} ({Currency(amount)} payment plus {Currency(convenienceFee)} convenience fee) was received. You can now review and record your agreement.";
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
        if (_currentStudentView.IsImpersonating) return Unauthorized();
        await OnGetAsync(term);
        if (LegacyApproval?.CountsAsApproved == true)
        {
            StatusMessage = "Your legacy bill approval is already recorded.";
            return RedirectToPage(new { term });
        }
        if (IsUnavailable || CurrentUser.Id is null || string.IsNullOrWhiteSpace(ExternalTermId) || Agreement is null)
        {
            StatusMessage = "Your agreement could not be saved. Please reload the page and try again.";
            return RedirectToPage(new { term });
        }

        if (string.IsNullOrEmpty(ReviewVersion) || ReviewVersion != CurrentReviewVersion)
        {
            ModelState.AddModelError(string.Empty, "Your bill changed or needs a fresh review. Review the updated amounts before approving.");
            return Page();
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

        if (paymentChoice == BillPaymentChoice.DeferredPaymentPlan && PaymentPlan is null)
        {
            ModelState.AddModelError(string.Empty, "The payment plan could not be calculated. Please reload before approving your bill.");
            return Page();
        }
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) return Unauthorized();
        var approval = await _billApprovals.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.ExternalTermId == ExternalTermId);
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
        approval.UpdateDraft(paymentChoice, ChargesTotal, CreditsTotal, PendingAidTotal,
            paymentChoice == BillPaymentChoice.DeferredPaymentPlan ? DeferredBalance : RemainingBalance,
            paymentChoice == BillPaymentChoice.DeferredPaymentPlan ? PaymentPlanFee : 0m, scheduleJson, reviewSnapshotJson);
        if (BillApprovalNotification.IsNonProduction(_configuration))
        {
            try { BillApprovalTestRecipient.Capture(approval, CurrentUser.Id.Value, CurrentUser.Email, _clock.Now); }
            catch (BillCompletionPreparationException exception)
            { ModelState.AddModelError(string.Empty, exception.Message); return Page(); }
        }
        approval.Accept(Agreement.Id, Agreement.Version, Agreement.ContentHtml, _clock.Now,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

        if (isNew)
            await _billApprovals.InsertAsync(approval, autoSave: true);
        else
            await _billApprovals.UpdateAsync(approval, autoSave: true);
        await _jobs.EnqueueAsync(new BillApprovalCompletionJobArgs(CurrentTenant.Id!.Value,approval.Id));
        StatusMessage = "Your bill is approved. Your document and completion notifications are being prepared.";
        return RedirectToPage(new { term });
    }

    public async Task<IActionResult> OnPostGeneratePdfAsync([FromQuery] string term)
    {
        if (_currentStudentView.IsImpersonating || CurrentUser.Id is null || CurrentTenant.Id is null || string.IsNullOrWhiteSpace(term)) return NotFound();
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) return NotFound();
        var approval = await _billApprovals.FindAsync(x => x.StudentProfileId == profile.Id && x.TermCode == term && x.AcceptedAt != null);
        if (approval is null) return NotFound();
        await _jobs.EnqueueAsync(new BillApprovalCompletionJobArgs(CurrentTenant.Id.Value,approval.Id));
        StatusMessage = "Pending completion work has been queued. Previously confirmed deliveries will not be repeated.";
        return RedirectToPage(new { term });
    }

    public async Task<IActionResult> OnGetPdfAsync([FromQuery] string term)
    {
        if (CurrentUser.Id is null || string.IsNullOrWhiteSpace(term)) return NotFound();
        var profile = await _currentStudentView.GetProfileAsync(HttpContext.RequestAborted);
        if (profile is null) return NotFound();
        var approval = await _billApprovals.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.TermCode == term && x.AcceptedAt != null);
        if (approval is null) return NotFound();
        var artifact = await _artifacts.FindAsync(x => x.BillApprovalId == approval.Id);
        if (artifact?.PdfStatus != BillArtifactOperationStatus.Completed ||
            string.IsNullOrWhiteSpace(artifact.PdfBlobName) || string.IsNullOrWhiteSpace(artifact.PdfFileName))
            return NotFound();
        var bytes = await _pdfs.GetAllBytesAsync(artifact.PdfBlobName, HttpContext.RequestAborted);
        return File(bytes, "application/pdf", artifact.PdfFileName);
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
        FinancialAidRules.IncludeInBillApproval(award);
}
