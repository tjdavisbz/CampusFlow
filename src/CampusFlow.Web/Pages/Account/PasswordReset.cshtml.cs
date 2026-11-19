using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.AccountRecovery;
using CampusFlow.AdvisorPortal;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CampusFlow.Web.AccountRecovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Volo.Abp.Auditing;

namespace CampusFlow.Web.Pages.Account;

[AllowAnonymous]
[RequireHttps]
[DisableAuditing]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PasswordResetModel(IAccountRecoveryWorkflow workflow, IConfiguration? config = null, ILogger<PasswordResetModel>? logger = null) : PageModel
{
    public const string RecoveryCookieName = "__Host-CFRecovery";
    [BindProperty] public RecoveryInput Input { get; set; } = new();
    public RecoveryStep Step { get; private set; } = RecoveryStep.Identity;
    public string? Question { get; private set; }
    public string? Feedback { get; private set; }

    public Task<IActionResult> OnGetAsync() => ExecuteAsync(() =>
        workflow.ResumeAsync(Ticket(), ClientAddress(), HttpContext.RequestAborted));

    public async Task<IActionResult> OnPostIdentityAsync()
    {
        if (!Prepare()) return DeniedOrUnavailable();
        if (!TryIdentity(out var identity))
        {
            ForgetTicket(); Step = RecoveryStep.Identity;
            Feedback = "Enter your first and last name, all nine Social Security number digits, and a valid date of birth.";
            ClearInputs(); return Page();
        }
        return await ExecuteAsync(() => workflow.StartAsync(identity!, ClientAddress(), HttpContext.RequestAborted));
    }

    public async Task<IActionResult> OnPostAnswerAsync()
    {
        if (!Prepare()) return DeniedOrUnavailable();
        if (string.IsNullOrWhiteSpace(Input.Answer) || Input.Answer.Length > 500)
        {
            var result = await ExecuteAsync(() => workflow.ResumeAsync(Ticket(), ClientAddress(), HttpContext.RequestAborted));
            if (Step == RecoveryStep.Question) Feedback = "Enter your answer to continue.";
            return result;
        }
        return await ExecuteAsync(() => workflow.AnswerAsync(Ticket(), Input.Answer, ClientAddress(), HttpContext.RequestAborted));
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!Prepare()) return DeniedOrUnavailable();
        var password = Input.Password ?? "";
        var confirmation = Input.Confirmation ?? "";
        if (password.Length is < 8 or > 256 || !string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            var result = await ExecuteAsync(() => workflow.ResumeAsync(Ticket(), ClientAddress(), HttpContext.RequestAborted));
            if (Step == RecoveryStep.Password) Feedback = "Enter a password of 8–256 characters and enter the same password again to confirm it.";
            return result;
        }
        return await ExecuteAsync(() => workflow.ResetAsync(Ticket(), password, confirmation,
            ClientAddress(), HttpContext.RequestAborted), RecoveryStep.Unknown);
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<AccountRecoveryResult>> action,
        RecoveryStep exceptionStep = RecoveryStep.Unavailable)
    {
        if (!Prepare()) return DeniedOrUnavailable();
        try { Apply(await action()); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch { Apply(new AccountRecoveryResult { Step = exceptionStep }); }
        finally { ClearInputs(); }
        return Page();
    }

    private bool Prepare()
    {
        Input ??= new RecoveryInput();
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        var available = workflow.IsAvailable;
        var trustedClient = !RecoveryClientAddress.RequiresTrustedProxy(config) || RecoveryClientAddress.Resolve(HttpContext, config!) is not null;
        var impersonating = IsImpersonating();
        if (HttpMethods.IsGet(Request.Method) && config?.GetValue<bool>("PasswordRecovery:Enabled") == true &&
            (!available || !trustedClient || impersonating))
            logger?.LogWarning("Account recovery request blocked: BackendAvailable={BackendAvailable}, TrustedClientResolved={TrustedClientResolved}, Impersonating={Impersonating}",
                available, trustedClient, impersonating);
        return !impersonating && available && trustedClient;
    }

    private IActionResult DeniedOrUnavailable()
    {
        ForgetTicket(); ClearInputs();
        if (IsImpersonating()) return Forbid();
        Apply(new AccountRecoveryResult { Step = RecoveryStep.Unavailable });
        return Page();
    }

    private bool IsImpersonating() => User.HasClaim(x => x.Type == StudentViewClaimTypes.ExternalStudentId ||
        x.Type == FacultyViewClaimTypes.FacultyId || x.Type == SchedulerViewClaimTypes.Email);

    private string ClientAddress() => RecoveryClientAddress.RequiresTrustedProxy(config)
        ? RecoveryClientAddress.Resolve(HttpContext, config!) ?? "unknown"
        : HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string? Ticket() => Request.Cookies.TryGetValue(RecoveryCookieName, out var value) && value.Length <= 3800 ? value : null;

    private static CookieOptions CookieOptions() => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true
    };

    private void ForgetTicket() => Response.Cookies.Delete(RecoveryCookieName, CookieOptions());

    private void Apply(AccountRecoveryResult result)
    {
        Step = Enum.IsDefined(result.Step) ? result.Step : RecoveryStep.Unavailable;
        Question = Step == RecoveryStep.Question ? result.Question : null;
        if (Step is RecoveryStep.Question or RecoveryStep.Password)
        {
            var ticket = result.Ticket ?? Ticket();
            if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 3800 ||
                Step == RecoveryStep.Question && string.IsNullOrWhiteSpace(Question))
            {
                Step = RecoveryStep.Unavailable; Question = null; ForgetTicket(); return;
            }
            if (result.Ticket is not null)
            {
                var options = CookieOptions(); options.MaxAge = TimeSpan.FromMinutes(10);
                Response.Cookies.Append(RecoveryCookieName, ticket, options);
            }
        }
        else ForgetTicket();

        // Only fixed notice codes reach the public page, never an upstream message.
        Feedback = result.Notice switch
        {
            RecoveryNotice.NotVerified => "We could not verify those details. Check the information and try again, or contact Nelson IT for help.",
            RecoveryNotice.RateLimited => "Several recovery requests were made recently. Please wait up to 15 minutes before starting another request, or contact Nelson IT for help.",
            RecoveryNotice.PasswordMismatch => "Enter the same new password in both password fields.",
            RecoveryNotice.PasswordPolicy => "That password was rejected by the university password policy. Choose a different password you have not used before. If you recently changed your password, you may need to wait or contact Nelson IT.",
            RecoveryNotice.Expired => "Your verification has expired. Enter your details again to continue.",
            _ => null
        };
    }

    private bool TryIdentity(out AccountRecoveryIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(Input.FirstName) || string.IsNullOrWhiteSpace(Input.LastName) ||
            Input.FirstName.Length > 100 || Input.LastName.Length > 100 ||
            !Digits(Input.SsnFirst, 3) || !Digits(Input.SsnMiddle, 2) || !Digits(Input.SsnLast, 4) ||
            !DateOnly.TryParseExact(Input.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var birthDate)) return false;
        identity = new AccountRecoveryIdentity { FirstName = Input.FirstName.Trim(), LastName = Input.LastName.Trim(),
            Ssn = Input.SsnFirst + Input.SsnMiddle + Input.SsnLast, BirthDate = birthDate };
        return true;
    }

    private static bool Digits(string? value, int length) => value is not null && value.Length == length && value.All(x => x is >= '0' and <= '9');

    private void ClearInputs()
    {
        Input ??= new RecoveryInput();
        Input.FirstName = Input.LastName = Input.SsnFirst = Input.SsnMiddle = Input.SsnLast =
            Input.BirthDate = Input.Answer = Input.Password = Input.Confirmation = "";
        Input = new RecoveryInput(); ModelState.Clear();
    }

    [DisableAuditing]
    public sealed class RecoveryInput
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string SsnFirst { get; set; } = "";
        public string SsnMiddle { get; set; } = "";
        public string SsnLast { get; set; } = "";
        public string BirthDate { get; set; } = "";
        public string Answer { get; set; } = "";
        public string Password { get; set; } = "";
        public string Confirmation { get; set; } = "";
        public override string ToString() => "Account recovery input [redacted]";
    }
}
