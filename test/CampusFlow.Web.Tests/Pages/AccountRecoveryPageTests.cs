using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HtmlAgilityPack;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AccountRecovery;
using CampusFlow.Students;
using CampusFlow.Web.Pages.Account;
using CampusFlow.Web.Portals;
using CampusFlow.AdvisorPortal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Volo.Abp.Auditing;
using Xunit;

namespace CampusFlow.Pages;

public sealed class AccountRecoveryPageTests
{
    private const string OpaqueTicket = "opaque-unit-test-cookie-1234567890";
    private const string SyntheticPassword = "UI-only-test-password-A1!";
    private static PasswordResetModel Page(FakeWorkflow workflow, bool withTicket = false)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");
        if (withTicket) context.Request.Headers.Cookie = PasswordResetModel.RecoveryCookieName + "=" + OpaqueTicket;
        return new PasswordResetModel(workflow) { PageContext = new PageContext { HttpContext = context } };
    }
    private static PasswordResetModel.RecoveryInput Identity() => new()
    { FirstName = "Synthetic", LastName = "Student", SsnFirst = "000", SsnMiddle = "00", SsnLast = "1234", BirthDate = "2000-01-02" };
    private static void AssertCleared(PasswordResetModel page, PasswordResetModel.RecoveryInput? old = null)
    {
        Assert.Empty(page.ModelState);
        foreach (var property in typeof(PasswordResetModel.RecoveryInput).GetProperties())
        { Assert.Equal("", property.GetValue(page.Input)); if (old is not null) Assert.Equal("", property.GetValue(old)); }
    }
    [Fact] public void Public_endpoint_explicitly_requires_https_csrf_and_suppresses_sensitive_auditing()
    {
        var type = typeof(PasswordResetModel);
        Assert.True(Attribute.IsDefined(type, typeof(AllowAnonymousAttribute)));
        Assert.True(Attribute.IsDefined(type, typeof(RequireHttpsAttribute)));
        Assert.True(Attribute.IsDefined(type, typeof(AutoValidateAntiforgeryTokenAttribute)));
        Assert.True(Attribute.IsDefined(type, typeof(DisableAuditingAttribute)));
        Assert.True(Attribute.IsDefined(typeof(PasswordResetModel.RecoveryInput), typeof(DisableAuditingAttribute)));
        Assert.DoesNotContain(type.GetProperties(), p => p.Name.Equals("Ticket", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public async Task Identity_post_uses_server_ip_and_clears_bound_values_and_modelstate()
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Question, Ticket = OpaqueTicket, Question = "Synthetic question?" } };
        var page = Page(fake); var input = Identity(); page.Input = input;
        page.Request.Headers["X-Forwarded-For"] = "203.0.113.99";
        page.ModelState.SetModelValue("Input.SsnFirst", "000", "000");
        page.ModelState.AddModelError("Input.Answer", "Synthetic secret must not survive rendering");
        Assert.IsType<PageResult>(await page.OnPostIdentityAsync());
        Assert.Equal(1, fake.Starts); Assert.Equal("000001234", fake.SeenIdentity!.Ssn);
        Assert.Equal(new DateOnly(2000, 1, 2), fake.SeenIdentity.BirthDate);
        Assert.Equal("203.0.113.8", fake.SeenAddress); Assert.Equal(RecoveryStep.Question, page.Step);
        AssertCleared(page, input);
    }
    [Theory]
    [InlineData("first")] [InlineData("last")] [InlineData("ssn-first")] [InlineData("ssn-middle")]
    [InlineData("ssn-last")] [InlineData("birth")]
    public async Task Malformed_identity_never_reaches_the_workflow(string invalid)
    {
        var fake = new FakeWorkflow(); var page = Page(fake); var input = Identity(); page.Input = input;
        switch (invalid) { case "first": input.FirstName = null!; break; case "last": input.LastName = ""; break;
            case "ssn-first": input.SsnFirst = null!; break; case "ssn-middle": input.SsnMiddle = "x2"; break;
            case "ssn-last": input.SsnLast = "12"; break; case "birth": input.BirthDate = "not-a-date"; break; }
        Assert.IsType<PageResult>(await page.OnPostIdentityAsync()); Assert.Equal(0, fake.Starts);
        Assert.Equal(RecoveryStep.Identity, page.Step); AssertCleared(page, input);
    }
    [Fact] public async Task Verification_ticket_is_only_written_to_a_host_scoped_secure_cookie()
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Password, Ticket = OpaqueTicket } };
        var page = Page(fake); await page.OnGetAsync(); var header = page.Response.Headers.SetCookie.ToString();
        Assert.Contains("__Host-CFRecovery=" + OpaqueTicket, header); Assert.Contains("secure", header.ToLowerInvariant());
        Assert.Contains("httponly", header.ToLowerInvariant()); Assert.Contains("samesite=strict", header.ToLowerInvariant());
        Assert.Contains("path=/", header); Assert.Contains("max-age=600", header); Assert.DoesNotContain("domain=", header.ToLowerInvariant());
    }
    [Theory] [InlineData(RecoveryStep.Complete)] [InlineData(RecoveryStep.Unknown)] [InlineData(RecoveryStep.Unavailable)]
    public async Task Terminal_steps_remove_the_ticket_and_erase_secrets(RecoveryStep step)
    {
        var fake = new FakeWorkflow { Next = new() { Step = step, Ticket = OpaqueTicket, Message = SyntheticPassword } };
        var page = Page(fake, true); var input = Identity(); input.Password = input.Confirmation = SyntheticPassword; page.Input = input;
        await page.OnPostResetAsync(); Assert.Equal(step, page.Step); AssertCleared(page, input);
        Assert.Contains("expires=", page.Response.Headers.SetCookie.ToString()); Assert.Null(page.Question);
        Assert.DoesNotContain(SyntheticPassword, page.Feedback ?? "");
    }
    [Theory] [InlineData(RecoveryNotice.NotVerified)] [InlineData(RecoveryNotice.RateLimited)]
    [InlineData(RecoveryNotice.PasswordMismatch)] [InlineData(RecoveryNotice.PasswordPolicy)] [InlineData(RecoveryNotice.Expired)]
    public async Task Public_feedback_uses_fixed_notices_and_never_an_upstream_message(RecoveryNotice notice)
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Identity, Notice = notice, Message = SyntheticPassword } };
        var page = Page(fake); page.Input = Identity(); await page.OnPostIdentityAsync();
        Assert.NotEmpty(page.Feedback!); Assert.DoesNotContain(SyntheticPassword, page.Feedback!); AssertCleared(page);
    }
    [Fact] public async Task An_unexpected_reset_exception_is_an_unknown_result_not_a_safe_retry()
    {
        var fake = new FakeWorkflow { ThrowReset = true }; var page = Page(fake, true);
        page.Input.Password = page.Input.Confirmation = SyntheticPassword;
        await page.OnPostResetAsync(); Assert.Equal(1, fake.Resets); Assert.Equal(RecoveryStep.Unknown, page.Step);
        Assert.Contains("expires=", page.Response.Headers.SetCookie.ToString()); AssertCleared(page);
    }
    [Fact] public async Task A_missing_confirmation_does_not_submit_a_password_update()
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Password } }; var page = Page(fake, true);
        page.Input.Password = SyntheticPassword; page.Input.Confirmation = null!;
        await page.OnPostResetAsync(); Assert.Equal(0, fake.Resets); Assert.Equal(1, fake.Resumes);
        Assert.Equal(RecoveryStep.Password, page.Step); Assert.NotNull(page.Feedback); AssertCleared(page);
    }
    [Fact] public async Task A_missing_password_is_handled_without_an_exception_or_workflow_write()
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Password } }; var page = Page(fake, true);
        page.Input.Password = null!; await page.OnPostResetAsync(); Assert.Equal(0, fake.Resets); AssertCleared(page);
    }
    [Fact] public async Task Resume_reads_the_ticket_from_the_cookie_without_exposing_it_in_the_model()
    {
        var fake = new FakeWorkflow { Next = new() { Step = RecoveryStep.Question, Question = "Synthetic question?" } };
        var page = Page(fake, true); await page.OnGetAsync(); Assert.Equal(OpaqueTicket, fake.SeenTicket);
        Assert.Equal(RecoveryStep.Question, page.Step); Assert.Null(page.Feedback);
    }
    [Fact] public async Task An_unavailable_service_cannot_receive_an_identity_or_reset_request()
    {
        var fake = new FakeWorkflow { IsAvailable = false }; var page = Page(fake, true); page.Input = Identity();
        await page.OnPostIdentityAsync(); Assert.Equal(RecoveryStep.Unavailable, page.Step); Assert.Equal(0, fake.Starts);
        Assert.Equal(0, fake.Resumes); AssertCleared(page);
    }
    [Theory]
    [InlineData(StudentViewClaimTypes.ExternalStudentId)] [InlineData(FacultyViewClaimTypes.FacultyId)] [InlineData(SchedulerViewClaimTypes.Email)]
    public async Task All_portal_view_identities_are_denied_even_for_get(string claim)
    {
        var fake = new FakeWorkflow(); var page = Page(fake, true);
        page.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim, "synthetic-view")], "Test"));
        Assert.IsType<ForbidResult>(await page.OnGetAsync()); Assert.Equal(0, fake.Resumes);
        Assert.Contains("expires=", page.Response.Headers.SetCookie.ToString());
    }
    [Fact] public async Task Recovery_responses_are_not_cached_or_sent_as_referrers()
    {
        var page = Page(new FakeWorkflow()); await page.OnGetAsync();
        Assert.Contains("no-store", page.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", page.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", page.Response.Headers["X-Frame-Options"].ToString());
        Assert.Contains("form-action 'self'", page.Response.Headers["Content-Security-Policy"].ToString());
    }
    private sealed class FakeWorkflow : IAccountRecoveryWorkflow
    {
        public bool IsAvailable { get; set; } = true;
        public AccountRecoveryResult Next { get; set; } = new() { Step = RecoveryStep.Identity };
        public int Starts, Resumes, Resets; public bool ThrowReset;
        public AccountRecoveryIdentity? SeenIdentity; public string? SeenAddress, SeenTicket;
        public Task<AccountRecoveryResult> ResumeAsync(string? ticket, string clientAddress, CancellationToken ct = default)
        { Resumes++; SeenAddress = clientAddress; SeenTicket = ticket; return Task.FromResult(Next); }
        public Task<AccountRecoveryResult> StartAsync(AccountRecoveryIdentity identity, string clientAddress, CancellationToken ct = default)
        { Starts++; SeenIdentity = identity; SeenAddress = clientAddress; return Task.FromResult(Next); }
        public Task<AccountRecoveryResult> AnswerAsync(string? ticket, string answer, string clientAddress, CancellationToken ct = default) => Task.FromResult(Next);
        public Task<AccountRecoveryResult> ResetAsync(string? ticket, string password, string confirmation, string clientAddress, CancellationToken ct = default)
        { Resets++; return ThrowReset ? Task.FromException<AccountRecoveryResult>(new InvalidOperationException(SyntheticPassword)) : Task.FromResult(Next); }
    }
}

public sealed class AccountRecoveryAnonymousPageTests : CampusFlowWebTestBase
{
    [Fact] public async Task Anonymous_shareable_url_renders_truthful_unavailability_without_a_login_redirect_or_form()
    {
        Client.BaseAddress = new Uri("https://localhost");
        var response = await Client.GetAsync("/Account/PasswordReset"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(); Assert.Contains("Online account recovery is currently unavailable", html);
        Assert.DoesNotContain("<form", html); Assert.DoesNotContain("__Host-CFRecovery", html);
        Assert.Contains("href=\"/Account/MicrosoftLogin\"", html); Assert.DoesNotContain("returnUrl=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
    [Fact] public async Task Unsafe_requests_without_csrf_are_rejected_before_any_recovery_action()
    {
        Client.BaseAddress = new Uri("https://localhost");
        var response = await Client.PostAsync("/Account/PasswordReset?handler=Reset",new FormUrlEncodedContent([]));
        // The existing ABP UI middleware turns a browser 400 into its standard error page redirect.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Error?httpStatusCode=400", response.Headers.Location?.OriginalString);
    }
}


public sealed class AccountRecoveryRenderedPageTests : CampusFlowWebTestBase
{
    private const string CookieTicket = "synthetic-render-ticket-that-must-never-appear-in-html";
    private const string Secret = "Synthetic-answer-and-password-must-not-appear";
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Replace(ServiceDescriptor.Transient<IAccountRecoveryWorkflow, RenderWorkflow>());
    }
    [Theory]
    [InlineData(RecoveryStep.Identity)] [InlineData(RecoveryStep.Question)] [InlineData(RecoveryStep.Password)]
    [InlineData(RecoveryStep.Complete)] [InlineData(RecoveryStep.Unknown)] [InlineData(RecoveryStep.Unavailable)]
    public async Task Real_Razor_rendering_never_puts_identity_secrets_or_the_recovery_ticket_in_html(RecoveryStep step)
    {
        Client.BaseAddress = new Uri("https://localhost");
        Client.DefaultRequestHeaders.Add("X-CF028-Test-Step",step.ToString());
        var response = await Client.GetAsync("/Account/PasswordReset?Input.Password="+Secret+"&Input.FirstName="+Secret);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret,html);Assert.DoesNotContain(CookieTicket,html);Assert.DoesNotContain("__Host-CFRecovery",html);
        var doc=new HtmlDocument();doc.LoadHtml(html);var forms=doc.DocumentNode.SelectNodes("//form");
        Assert.DoesNotContain("Microsoft", HtmlEntity.DeEntitize(doc.DocumentNode.InnerText), StringComparison.OrdinalIgnoreCase);
        var style = Assert.Single(doc.DocumentNode.SelectNodes("//link[@rel='stylesheet']")!);
        var styleUrl = style.GetAttributeValue("href", "");
        Assert.Matches(@"^/css/account-recovery(?:\.[a-zA-Z0-9]+)?\.css(?:\?v=[a-zA-Z0-9_-]+)?$", styleUrl);
        var stylesheet = await Client.GetAsync(styleUrl);
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--recovery-purple:#4f2d7f", await stylesheet.Content.ReadAsStringAsync());
        if(step is RecoveryStep.Identity or RecoveryStep.Question or RecoveryStep.Password)
        {
            Assert.Single(forms!);Assert.Equal("post",forms![0].GetAttributeValue("method","").ToLowerInvariant());
            Assert.NotNull(forms[0].SelectSingleNode(".//input[@name='__RequestVerificationToken']"));
            foreach(var input in forms[0].SelectNodes(".//input[not(@name='__RequestVerificationToken')]"))Assert.Equal("",input.GetAttributeValue("value",""));
        }
        else Assert.Null(forms);
        if(step==RecoveryStep.Identity)
        {
            foreach(var name in new[]{"SsnFirst","SsnMiddle","SsnLast"})Assert.Equal("password",doc.DocumentNode.SelectSingleNode("//input[@name='Input."+name+"']")!.GetAttributeValue("type",""));
            Assert.Null(doc.DocumentNode.SelectSingleNode("//input[@name='Input.Password']"));
        }
        if(step==RecoveryStep.Question)Assert.Null(doc.DocumentNode.SelectSingleNode("//input[@name='Input.Password']"));
        if(step==RecoveryStep.Password)Assert.Equal(2,doc.DocumentNode.SelectNodes("//input[@type='password']")!.Count);
        Assert.Single(doc.DocumentNode.SelectNodes("//a[@href='/Account/MicrosoftLogin']")!);
        if(Environment.GetEnvironmentVariable("CF028_UI_HTML_DIR") is {Length:>0} dir)
        {Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,step.ToString().ToLowerInvariant()+".html"),html);}
    }
    [Fact] public async Task Valid_csrf_form_post_can_complete_without_echoing_either_password()
    {
        Client.BaseAddress=new Uri("https://localhost");Client.DefaultRequestHeaders.Add("X-CF028-Test-Step","Password");
        var response=await Client.GetAsync("/Account/PasswordReset");var doc=new HtmlDocument();doc.LoadHtml(await response.Content.ReadAsStringAsync());
        var token=doc.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']")!.GetAttributeValue("value","");
        var posted=await Client.PostAsync("/Account/PasswordReset?handler=Reset",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["Input.Password"]=Secret,["Input.Confirmation"]=Secret}));
        Assert.Equal(HttpStatusCode.OK,posted.StatusCode);var html=await posted.Content.ReadAsStringAsync();
        Assert.Contains("Your password was updated",html);Assert.DoesNotContain(Secret,html);Assert.DoesNotContain(CookieTicket,html);
        Assert.Contains(posted.Headers.GetValues("Set-Cookie"),x=>x.StartsWith("__Host-CFRecovery=",StringComparison.Ordinal)&&x.Contains("expires=",StringComparison.Ordinal));
    }
    private sealed class RenderWorkflow(IHttpContextAccessor contexts):IAccountRecoveryWorkflow
    {
        public bool IsAvailable=>true;
        private AccountRecoveryResult Result()=>new(){Step=Enum.Parse<RecoveryStep>(contexts.HttpContext!.Request.Headers["X-CF028-Test-Step"].ToString()),Ticket=CookieTicket,Question="Which city were you born in?",Message=Secret,Notice=RecoveryNotice.None};
        public Task<AccountRecoveryResult> ResumeAsync(string? ticket,string address,CancellationToken ct=default)=>Task.FromResult(Result());
        public Task<AccountRecoveryResult> StartAsync(AccountRecoveryIdentity identity,string address,CancellationToken ct=default)=>Task.FromResult(Result());
        public Task<AccountRecoveryResult> AnswerAsync(string? ticket,string answer,string address,CancellationToken ct=default)=>Task.FromResult(Result());
        public Task<AccountRecoveryResult> ResetAsync(string? ticket,string password,string confirmation,string address,CancellationToken ct=default)=>Task.FromResult(new AccountRecoveryResult{Step=RecoveryStep.Complete,Message=Secret});
    }
}
