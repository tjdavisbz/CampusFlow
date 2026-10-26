using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Identity;
using CampusFlowIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CampusFlow.Web.Pages.Faculty;

[Authorize]
public class EmailStudentsModel(
    FacultyPortalReader facultyReader,
    FacultyMailConnection mailConnection,
    FacultyGraphMailSender mailSender,
    IdentityUserManager userManager,
    SignInManager<CampusFlowIdentityUser> signInManager,
    ICurrentStudentView currentStudentView,
    ILogger<EmailStudentsModel> logger) : CampusFlowPageModel
{
    private const string Provider = "MicrosoftEntra";
    private const int MaximumRecipients = 100;
    private const string PreviewKey = "FacultyMailPreview";

    [BindProperty(SupportsGet = true)] public int? Term { get; set; }
    [BindProperty(SupportsGet = true)] public int? Course { get; set; }
    [BindProperty] public List<int> StudentUids { get; set; } = [];
    [BindProperty] public string Subject { get; set; } = string.Empty;
    [BindProperty] public string Body { get; set; } = string.Empty;

    public FacultyIdentity Faculty { get; private set; } = null!;
    public IReadOnlyList<FacultyTerm> Terms { get; private set; } = [];
    public FacultyTerm? SelectedTerm { get; private set; }
    public IReadOnlyList<FacultyCourse> Courses { get; private set; } = [];
    public FacultyCourse? SelectedCourse { get; private set; }
    public IReadOnlyList<FacultyEmailRecipient> Roster { get; private set; } = [];
    public bool MailConnected { get; private set; }
    public bool PreviewReady { get; private set; }
    public int RecipientCount { get; private set; }
    public string? Notice { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return Forbid();
        Notice = TempData["FacultyMailNotice"] as string;
        if (SelectedCourse is not null && !MailConnected &&
            !string.Equals(Request.Query["mailSetup"], "failed", StringComparison.Ordinal))
            return BeginMailAuthorization();
        return Page();
    }

    public async Task<IActionResult> OnPostConnectAsync()
    {
        if (!await LoadAsync() || FacultyViewSession.GetFacultyId(HttpContext).HasValue) return Forbid();
        return BeginMailAuthorization();
    }

    public async Task<IActionResult> OnGetConnectedAsync()
    {
        if (!await LoadAsync() || FacultyViewSession.GetFacultyId(HttpContext).HasValue) return Forbid();
        if (CurrentUser.Id is not Guid userId) return Forbid();
        var external = await signInManager.GetExternalLoginInfoAsync(userId.ToString());
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
            !DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var expiresAt))
        {
            logger.LogWarning("Faculty mail connection did not match the signed-in Microsoft account. UserId={UserId}", userId);
            TempData["FacultyMailNotice"] = "Microsoft could not authorize email for your portal account. Please contact Nelson IT if this continues.";
            return RedirectToPage(new { term = SelectedTerm?.Id, course = SelectedCourse?.OfferId, mailSetup = "failed" });
        }
        try
        {
            await mailConnection.ConnectAsync(userId, linkedKey, senderEmail!, accessToken,
                refreshToken, expiresAt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Faculty mail authorization could not be saved for user {UserId}.", userId);
            TempData["FacultyMailNotice"] = "Email setup could not be saved. Please contact Nelson IT.";
            return RedirectToPage(new { term = SelectedTerm?.Id, course = SelectedCourse?.OfferId, mailSetup = "failed" });
        }
        return RedirectToPage(new { term = SelectedTerm?.Id, course = SelectedCourse?.OfferId });
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!await LoadAsync() || FacultyViewSession.GetFacultyId(HttpContext).HasValue) return Forbid();
        var recipients = ValidateDraft();
        if (recipients is null) return Page();
        if (!MailConnected)
        {
            ModelState.AddModelError(string.Empty, "Microsoft mail is temporarily unavailable. Reload the page to continue.");
            return Page();
        }
        RecipientCount = recipients.Count;
        PreviewReady = true;
        TempData[PreviewKey] = Fingerprint(recipients);
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!await LoadAsync() || FacultyViewSession.GetFacultyId(HttpContext).HasValue) return Forbid();
        var recipients = ValidateDraft();
        if (recipients is null) return Page();
        var expected = TempData[PreviewKey] as string; // Normal browser retries must be previewed again.
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),
                Convert.FromHexString(Fingerprint(recipients))))
        {
            ModelState.AddModelError(string.Empty, "Review this message again before sending.");
            return Page();
        }
        if (CurrentUser.Id is not Guid userId) return Forbid();
        var linkedKey = await GetLinkedProviderKeyAsync(userId);
        var access = linkedKey is null ? null : await mailConnection.GetAccessAsync(
            userId, linkedKey, HttpContext.RequestAborted);
        if (access is null)
        {
            ModelState.AddModelError(string.Empty, "Microsoft mail authorization needs renewal. Reload the page and review the message again.");
            MailConnected = false;
            return Page();
        }
        var result = await mailSender.SendAsync(access.AccessToken, access.SenderEmail,
            Subject.Trim(), Body.Trim(), recipients, HttpContext.RequestAborted);
        if (!result.Accepted)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The message was not accepted.");
            if (result.AuthorizationFailed) MailConnected = false;
            return Page();
        }
        logger.LogInformation("Faculty {FacultyId} submitted mail for {RecipientCount} students in offer {OfferId}. TraceId={TraceId}",
            Faculty.FacultyId, recipients.Count, SelectedCourse!.OfferId, HttpContext.TraceIdentifier);
        TempData["FacultyMailNotice"] = $"Microsoft accepted the message for {recipients.Count} students. Delivery is not yet confirmed.";
        return RedirectToPage(new { term = SelectedTerm?.Id, course = SelectedCourse?.OfferId });
    }

    private async Task<bool> LoadAsync()
    {
        if (currentStudentView.IsImpersonating) return false;
        // Unlike the read-only faculty pages, this page never resolves a viewed faculty ID.
        if (FacultyViewSession.GetFacultyId(HttpContext).HasValue) return false;
        var faculty = await facultyReader.FindFacultyAsync(CurrentUser.Email, HttpContext.RequestAborted);
        if (faculty is null) return false;
        Faculty = faculty;
        Terms = await facultyReader.GetTermsAsync(faculty.FacultyId, HttpContext.RequestAborted);
        var today = DateTime.Today;
        SelectedTerm = Terms.FirstOrDefault(x => x.Id == Term)
            ?? Terms.FirstOrDefault(x => x.StartsAt <= today && x.EndsAt >= today)
            ?? Terms.FirstOrDefault();
        if (SelectedTerm is not null)
        {
            Term = SelectedTerm.Id;
            Courses = await facultyReader.GetCoursesAsync(faculty.FacultyId, SelectedTerm.Id, HttpContext.RequestAborted);
            SelectedCourse = Courses.FirstOrDefault(x => x.OfferId == Course) ?? Courses.FirstOrDefault();
            if (SelectedCourse is not null)
            {
                Course = SelectedCourse.OfferId;
                Roster = await facultyReader.GetEmailRecipientsAsync(faculty.FacultyId,
                    SelectedTerm.Id, SelectedCourse.OfferId, HttpContext.RequestAborted);
            }
        }
        if (CurrentUser.Id is Guid userId)
        {
            var linkedKey = await GetLinkedProviderKeyAsync(userId);
            MailConnected = linkedKey is not null && await mailConnection.GetAccessAsync(
                userId, linkedKey, HttpContext.RequestAborted) is not null;
        }
        return true;
    }

    private IActionResult BeginMailAuthorization()
    {
        if (CurrentUser.Id is not Guid userId) return Forbid();
        var callback = Url.Page("/Faculty/EmailStudents", "Connected",
            new { term = SelectedTerm?.Id, course = SelectedCourse?.OfferId });
        var baseline = signInManager.ConfigureExternalAuthenticationProperties(Provider, callback, userId.ToString());
        var properties = new OpenIdConnectChallengeProperties(baseline.Items, baseline.Parameters)
        {
            RedirectUri = baseline.RedirectUri
        };
        properties.SetScope(["openid", "profile", "email", "offline_access",
            "https://graph.microsoft.com/Mail.Send"]);
        return Challenge(properties, Provider);
    }

    private List<string>? ValidateDraft()
    {
        if (SelectedCourse is null) ModelState.AddModelError(string.Empty, "Choose a course.");
        if (string.IsNullOrWhiteSpace(Subject) || Subject.Length > 200)
            ModelState.AddModelError(nameof(Subject), "Enter a subject of 1–200 characters.");
        if (string.IsNullOrWhiteSpace(Body) || Body.Length > 10000)
            ModelState.AddModelError(nameof(Body), "Enter a message of 1–10,000 characters.");
        var chosen = StudentUids.Distinct().ToArray();
        if (chosen.Length is < 1 or > MaximumRecipients)
            ModelState.AddModelError(nameof(StudentUids), "Choose between 1 and 100 students.");
        var matches = Roster.Where(x => chosen.Contains(x.StudentUid)).ToArray();
        if (matches.Length != chosen.Length)
            ModelState.AddModelError(nameof(StudentUids), "The selection contains a student outside this course. Refresh and try again.");
        if (matches.Any(x => !IsPlainEmail(x.Email)))
            ModelState.AddModelError(nameof(StudentUids), "A selected student has no valid active local email address.");
        var emails = matches.Select(x => x.Email!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (emails.Count != matches.Length)
            ModelState.AddModelError(nameof(StudentUids), "Two selected students share an email address. Please review the roster.");
        return ModelState.IsValid ? emails : null;
    }

    private string Fingerprint(IReadOnlyList<string> recipients)
    {
        var text = JsonSerializer.Serialize(new
        {
            CurrentUser.Id,
            TermId = SelectedTerm?.Id,
            OfferId = SelectedCourse?.OfferId,
            Recipients = recipients.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Subject = Subject.Trim(),
            Body = Body.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool IsPlainEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        MailAddress.TryCreate(value, out var parsed) &&
        string.Equals(value.Trim(), parsed.Address, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> GetLinkedProviderKeyAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var logins = await userManager.GetLoginsAsync(user);
        return logins.SingleOrDefault(x => x.LoginProvider == Provider)?.ProviderKey;
    }
}
