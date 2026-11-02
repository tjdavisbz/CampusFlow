using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using CampusFlowIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CampusFlow.Web.Pages.Advisor;

[Authorize]
public class ReviewModel : CampusFlowPageModel
{
    private const string Provider = "MicrosoftEntra";
    private const string PreviewKey = "AdvisorReviewEmailPreview";
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly ITenantThemeProvider _tenantThemeProvider;
    private readonly FacultyMailConnection _mailConnection;
    private readonly AdvisorReviewMailDraftStore _mailDrafts;
    private readonly FacultyGraphMailSender _mailSender;
    private readonly IdentityUserManager _userManager;
    private readonly SignInManager<CampusFlowIdentityUser> _signInManager;
    private readonly IRepository<CourseReviewSubmission, Guid> _submissions;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(IAdvisorPortalAppService advisorPortal, ITenantThemeProvider tenantThemeProvider,
        FacultyMailConnection mailConnection, AdvisorReviewMailDraftStore mailDrafts,
        FacultyGraphMailSender mailSender,
        IdentityUserManager userManager, SignInManager<CampusFlowIdentityUser> signInManager,
        IRepository<CourseReviewSubmission, Guid> submissions, ILogger<ReviewModel> logger)
    {
        _advisorPortal = advisorPortal;
        _tenantThemeProvider = tenantThemeProvider;
        _mailConnection = mailConnection;
        _mailDrafts = mailDrafts;
        _mailSender = mailSender;
        _userManager = userManager;
        _signInManager = signInManager;
        _submissions = submissions;
        _logger = logger;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public AdvisorStudentReviewDto Review { get; private set; } = new();
    public string? EmailPreviewHtml { get; private set; }
    public string? EmailSubject { get; private set; }
    public bool PreviewReady { get; private set; }
    public bool MailConnected { get; private set; }
    public Guid? FailedEmailSubmissionId { get; private set; }
    public string? EmailNotice { get; private set; }

    [BindProperty]
    public SubmitAdvisorReviewInput Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid studentProfileId, string term)
    {
        if (!await _advisorPortal.HasAccessAsync()) return Forbid();
        await LoadAsync(studentProfileId, term);
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!await _advisorPortal.HasAccessAsync()) return Forbid();
        await LoadAsync(Input.StudentProfileId, Input.ExternalTermId, populateInput: false);
        var email = await _advisorPortal.PreviewEmailAsync(Input);
        if (!MailAddress.TryCreate(email.StudentEmail, out _))
        {
            ModelState.AddModelError(string.Empty, "The student's email address is not available for delivery.");
            return Page();
        }
        if (!MailConnected)
        {
            if (CurrentUser.Id is not Guid userId) return Forbid();
            var draftId = await _mailDrafts.SaveAsync(userId, CurrentTenant.Id, Input,
                HttpContext.RequestAborted);
            return BeginMailAuthorization(draftId: draftId);
        }
        ShowPreview(email);
        return Page();
    }

    private void ShowPreview(AdvisorReviewEmailDto email)
    {
        EmailSubject = AdvisorReviewEmailTemplate.Subject(email);
        EmailPreviewHtml = AdvisorReviewEmailTemplate.Render(email);
        PreviewReady = true;
        TempData[PreviewKey] = Fingerprint();
    }

    public async Task<IActionResult> OnPostSubmitAsync()
    {
        if (!await _advisorPortal.HasAccessAsync()) return Forbid();
        await LoadAsync(Input.StudentProfileId, Input.ExternalTermId, populateInput: false);
        var expected = TempData[PreviewKey] as string;
        if (string.IsNullOrWhiteSpace(expected) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),
                Convert.FromHexString(Fingerprint())))
        {
            ModelState.AddModelError(string.Empty, "Preview the current decisions and message before saving.");
            return Page();
        }
        var access = await GetMailAccessAsync();
        if (access is null)
        {
            ModelState.AddModelError(string.Empty,
                "Microsoft mail authorization expired. Select Preview email again to reconnect before saving.");
            return Page();
        }
        var preview = await _advisorPortal.PreviewEmailAsync(Input);
        if (!MailAddress.TryCreate(preview.StudentEmail, out _))
        {
            ModelState.AddModelError(string.Empty, "The student's email address is not available for delivery.");
            return Page();
        }
        var submissionId = await _advisorPortal.SubmitAsync(Input);
        preview.SubmissionId = submissionId;
        return await DeliverAsync(submissionId, preview, access);
    }

    public async Task<IActionResult> OnGetEmailRetryAsync(Guid id)
    {
        var email = await _advisorPortal.GetSubmissionEmailAsync(id);
        if (email.EmailStatus == nameof(ReviewEmailStatus.Sent)) return RedirectToPage("/Advisor/Index");
        await ShowRetryAsync(id, email, null);
        return Page();
    }

    public async Task<IActionResult> OnPostRetryEmailAsync(Guid submissionId)
    {
        var email = await _advisorPortal.GetSubmissionEmailAsync(submissionId);
        if (email.EmailStatus == nameof(ReviewEmailStatus.Sent)) return RedirectToPage("/Advisor/Index");
        var access = await GetMailAccessAsync();
        if (access is null)
        {
            Input.ExternalTermId = email.ExternalTermId;
            return BeginMailAuthorization(submissionId: submissionId);
        }
        return await DeliverAsync(submissionId, email, access);
    }

    private IActionResult BeginMailAuthorization(Guid? draftId = null, Guid? submissionId = null)
    {
        if (CurrentUser.Id is not Guid userId) return Forbid();
        var callback = Url.Page("/Advisor/Review", "Connected",
            new { studentProfileId = Input.StudentProfileId, term = Input.ExternalTermId,
                draftId, submissionId });
        var baseline = _signInManager.ConfigureExternalAuthenticationProperties(Provider, callback, userId.ToString());
        var properties = new OpenIdConnectChallengeProperties(baseline.Items, baseline.Parameters)
        {
            RedirectUri = baseline.RedirectUri
        };
        properties.SetScope(["openid", "profile", "email", "offline_access",
            "https://graph.microsoft.com/Mail.Send"]);
        return Challenge(properties, Provider);
    }

    public async Task<IActionResult> OnGetConnectedAsync(Guid studentProfileId, string term,
        Guid? draftId, Guid? submissionId)
    {
        if (!await _advisorPortal.HasAccessAsync() || CurrentUser.Id is not Guid userId) return Forbid();
        var draft = draftId.HasValue
            ? await _mailDrafts.TakeAsync(draftId.Value, userId, CurrentTenant.Id, HttpContext.RequestAborted)
            : null;
        var external = await _signInManager.GetExternalLoginInfoAsync(userId.ToString());
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var linkedKey = await GetLinkedProviderKeyAsync(userId);
        var providerKey = external?.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessToken = ticket.Properties?.GetTokenValue("access_token");
        var refreshToken = ticket.Properties?.GetTokenValue("refresh_token");
        var expiresText = ticket.Properties?.GetTokenValue("expires_at");
        var senderEmail = external?.Principal.FindFirstValue("preferred_username") ??
                          external?.Principal.FindFirstValue(ClaimTypes.Email);
        if (external?.LoginProvider != Provider || string.IsNullOrWhiteSpace(linkedKey) ||
            !string.Equals(providerKey, linkedKey, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken) ||
            !MailAddress.TryCreate(senderEmail, out _) ||
            !DateTimeOffset.TryParse(expiresText, out var expiresAt))
            return await MailAuthorizationFailedAsync(draft, submissionId, studentProfileId, term,
                "Microsoft mail authorization did not complete. Select Preview email to try again.");
        try
        {
            await _mailConnection.ConnectAsync(userId, linkedKey, senderEmail!, accessToken,
                refreshToken, expiresAt);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save Microsoft mail authorization for advisor {UserId}.", userId);
            return await MailAuthorizationFailedAsync(draft, submissionId, studentProfileId, term,
                "Microsoft mail authorization could not be saved. Select Preview email to try again or contact Nelson IT.");
        }
        if (draftId.HasValue)
        {
            if (draft is null)
            {
                SuccessMessage = "The review preview expired during Microsoft authorization. Please enter your decisions again.";
                return RedirectToPage(new { studentProfileId, term });
            }
            Input = draft;
            await LoadAsync(draft.StudentProfileId, draft.ExternalTermId, populateInput: false);
            var email = await _advisorPortal.PreviewEmailAsync(Input);
            if (!MailAddress.TryCreate(email.StudentEmail, out _))
            {
                ModelState.AddModelError(string.Empty,
                    "The student's email address is not available for delivery.");
                return Page();
            }
            ShowPreview(email);
            return Page();
        }
        return submissionId.HasValue
            ? RedirectToPage("/Advisor/Review", "EmailRetry", new { id = submissionId.Value })
            : RedirectToPage(new { studentProfileId, term });
    }

    private async Task<IActionResult> MailAuthorizationFailedAsync(SubmitAdvisorReviewInput? draft,
        Guid? submissionId, Guid studentProfileId, string term, string message)
    {
        if (draft is not null)
        {
            Input = draft;
            await LoadAsync(draft.StudentProfileId, draft.ExternalTermId, populateInput: false);
            EmailNotice = message;
            return Page();
        }
        SuccessMessage = message;
        return submissionId.HasValue
            ? RedirectToPage("/Advisor/Review", "EmailRetry", new { id = submissionId.Value })
            : RedirectToPage(new { studentProfileId, term });
    }

    private async Task LoadAsync(Guid studentProfileId, string term, bool populateInput = true)
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        Review = await _advisorPortal.GetStudentReviewAsync(studentProfileId, term);
        MailConnected = await GetMailAccessAsync() is not null;
        if (!populateInput) return;
        Input.StudentProfileId = studentProfileId;
        Input.ExternalTermId = term;
        Input.Courses.Clear();
        foreach (var course in Review.Courses)
        {
            Input.Courses.Add(new AdvisorCourseDecisionInput
            {
                ReviewId = course.ReviewId,
                Comment = course.AdvisorComment
            });
        }
    }

    private async Task<IActionResult> DeliverAsync(Guid submissionId,
        AdvisorReviewEmailDto email, FacultyMailAccess access)
    {
        var result = await _mailSender.SendAsync(access.AccessToken, access.SenderEmail,
            AdvisorReviewEmailTemplate.Subject(email), AdvisorReviewEmailTemplate.Render(email),
            [email.StudentEmail], HttpContext.RequestAborted, html: true, directToRecipients: true);
        try
        {
            var submission = await _submissions.GetAsync(submissionId);
            if (submission.SubmittedByUserId != CurrentUser.Id) return Forbid();
            submission.RecordEmailResult(Clock.Now, result.Accepted, result.Error);
            await _submissions.UpdateAsync(submission, autoSave: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not record the mail result for advisor review {SubmissionId}.", submissionId);
            if (result.Accepted)
            {
                SuccessMessage = "Microsoft accepted the review email, but CampusFlow could not record that confirmation. Check Sent Items before trying to resend it.";
                return RedirectToPage("/Advisor/Index");
            }
            await ShowRetryAsync(submissionId, email,
                "The review was saved, but CampusFlow could not confirm email delivery. Check Sent Items before retrying.");
            return Page();
        }
        if (result.Accepted)
        {
            SuccessMessage = "The course review was saved, and Microsoft accepted the email for delivery.";
            return RedirectToPage("/Advisor/Index");
        }
        _logger.LogWarning("Advisor review {SubmissionId} was saved but its email was not accepted.", submissionId);
        await ShowRetryAsync(submissionId, email,
            $"The review was saved, but the email was not confirmed: {result.Error} Check Sent Items before retrying if the request timed out.");
        return Page();
    }

    private async Task ShowRetryAsync(Guid submissionId, AdvisorReviewEmailDto email, string? notice)
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        Review = new AdvisorStudentReviewDto { StudentName = email.StudentName,
            ExternalTermId = email.ExternalTermId, StudentEmail = email.StudentEmail };
        FailedEmailSubmissionId = submissionId;
        EmailSubject = AdvisorReviewEmailTemplate.Subject(email);
        EmailPreviewHtml = AdvisorReviewEmailTemplate.Render(email);
        EmailNotice = notice ?? "The review is saved, but the email has not been confirmed. Check Sent Items before retrying.";
        MailConnected = await GetMailAccessAsync() is not null;
    }

    private string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { CurrentUser.Id, Input.StudentProfileId, Input.ExternalTermId,
            Input.OverallComment, Input.Courses }))));

    private async Task<FacultyMailAccess?> GetMailAccessAsync()
    {
        if (CurrentUser.Id is not Guid userId) return null;
        var key = await GetLinkedProviderKeyAsync(userId);
        return key is null ? null : await _mailConnection.GetAccessAsync(userId, key, HttpContext.RequestAborted);
    }

    private async Task<string?> GetLinkedProviderKeyAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var logins = await _userManager.GetLoginsAsync(user);
        return logins.SingleOrDefault(x => x.LoginProvider == Provider)?.ProviderKey;
    }
}
