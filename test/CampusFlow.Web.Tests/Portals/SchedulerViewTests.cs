using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.Portals;

public class SchedulerViewTests
{
    private readonly ICurrentUser _actor = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPermissionChecker _permissions = Substitute.For<IPermissionChecker>();
    private readonly IRepository<AdvisorAssignment, Guid> _assignments = Substitute.For<IRepository<AdvisorAssignment, Guid>>();
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly SchedulerViewSession _session;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly string _cookie;
    public SchedulerViewTests()
    {
        _actor.Id.Returns(_actorId); _actor.Email.Returns("admin@example.test"); _tenant.Id.Returns(Guid.NewGuid());
        _session = new(_protection, _actor, _tenant);
        _permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default).Returns(true);
        var active = new List<AdvisorAssignment>{ new(Guid.NewGuid(), _tenant.Id, "Residential", "1", "scheduler@example.test", "Scheduler", DateTime.UtcNow.AddDays(-1)) };
        _assignments.GetListAsync(Arg.Any<Expression<Func<AdvisorAssignment,bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(active);
        var start = new DefaultHttpContext(); start.Request.IsHttps = true;
        _session.Start(start, new("1", "scheduler", "scheduler@example.test", "Test", "Scheduler", "Test Scheduler"));
        _cookie = start.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.Contains("httponly", start.Response.Headers.SetCookie.ToString().ToLowerInvariant());
        Assert.Contains("secure", start.Response.Headers.SetCookie.ToString().ToLowerInvariant());
    }
    private DefaultHttpContext Request(string method = "GET", string path = "/Advisor")
    {
        var context = new DefaultHttpContext(); context.Request.Method = method; context.Request.Path = path;
        context.Request.Headers.Cookie = _cookie; context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _actorId.ToString()), new Claim(ClaimTypes.Email, "admin@example.test")], "ActualActor"));
        return context;
    }
    private async Task<bool> Run(DefaultHttpContext context)
    {
        var reached = false;
        await new SchedulerViewContextMiddleware(_ => { reached = true; return Task.CompletedTask; })
            .InvokeAsync(context, _session, _permissions, _assignments, NullLogger<SchedulerViewContextMiddleware>.Instance);
        return reached;
    }
    [Fact]
    public async Task View_uses_the_target_email_without_replacing_the_actor()
    {
        var context = Request(); Assert.True(await Run(context));
        var view = new CurrentSchedulerView(new HttpContextAccessor{HttpContext=context});
        Assert.True(view.IsActive); Assert.Equal("scheduler@example.test", view.Email);
        Assert.Equal("admin@example.test", context.User.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(_actorId, _actor.Id);
    }
    [Theory]
    [InlineData("POST", "/Advisor/Review", false)]
    [InlineData("PUT", "/api/app/advisor-portal", false)]
    [InlineData("DELETE", "/api/other", false)]
    [InlineData("PATCH", "/Admin/StudentBilling/CostTypes", false)]
    [InlineData("POST", "/Admin/ImpersonateScheduler/End/extra", false)]
    [InlineData("POST", "/Admin/ImpersonateScheduler/End", true)]
    [InlineData("GET", "/Advisor/Review", true)]
    public async Task Only_reads_and_the_exact_exit_action_are_allowed(string method, string path, bool expected)
    {
        var context = Request(method, path); Assert.Equal(expected, await Run(context));
        Assert.Equal(expected ? 200 : 403, context.Response.StatusCode);
    }
    [Theory]
    [InlineData("Connected")]
    [InlineData("EmailRetry")]
    public async Task GET_mail_handlers_are_blocked(string handler)
    {
        var context = Request("GET", "/Advisor/Review"); context.Request.QueryString = new("?handler=" + handler);
        Assert.False(await Run(context)); Assert.Equal(403, context.Response.StatusCode);
    }
    [Theory]
    [InlineData("permission")]
    [InlineData("assignment")]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("tamper")]
    [InlineData("expired")]
    public async Task Revoked_or_invalid_views_end_without_adding_a_target_identity(string reason)
    {
        var context = Request();
        if (reason == "permission") _permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default).Returns(false);
        if (reason == "assignment") _assignments.GetListAsync(Arg.Any<Expression<Func<AdvisorAssignment,bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AdvisorAssignment>());
        if (reason == "actor") _actor.Id.Returns(Guid.NewGuid());
        if (reason == "tenant") _tenant.Id.Returns(Guid.NewGuid());
        if (reason == "tamper") context.Request.Headers.Cookie = SchedulerViewSession.CookieName + "=invalid";
        if (reason == "expired") context.Request.Headers.Cookie = SchedulerViewSession.CookieName + "=" +
            _protection.CreateProtector("CampusFlow.SchedulerView.v1").Protect(System.Text.Json.JsonSerializer.Serialize(new {
                ActorUserId = _actorId, TenantId = _tenant.Id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), Email = "scheduler@example.test", Name = "Scheduler" }));
        Assert.True(await Run(context));
        Assert.False(new CurrentSchedulerView(new HttpContextAccessor{HttpContext=context}).IsActive);
        Assert.Contains(SchedulerViewSession.CookieName, context.Response.Headers.SetCookie.ToString());
    }
    [Fact]
    public async Task A_write_from_an_expired_or_tampered_view_cannot_fall_back_to_the_administrators_access()
    {
        var context = Request("POST", "/Advisor/Review");
        context.Request.Headers.Cookie = SchedulerViewSession.CookieName + "=invalid";
        Assert.False(await Run(context)); Assert.Equal(403, context.Response.StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Starting_a_view_requires_its_own_permission_and_an_active_assignment(bool permission)
    {
        var lookup = Substitute.For<CampusFlow.StudentInformationSystems.IStudentInformationSystemAdvisorLookup>();
        lookup.FindAsync("other@example.test", Arg.Any<CancellationToken>()).Returns(new CampusFlow.StudentInformationSystems.AdvisorLookupResult("2","other","other@example.test","Other","Scheduler","Other Scheduler"));
        _permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default).Returns(permission);
        var lazy = Substitute.For<Volo.Abp.DependencyInjection.IAbpLazyServiceProvider>();
        var clock = Substitute.For<Volo.Abp.Timing.IClock>(); clock.Now.Returns(DateTime.UtcNow);
        lazy.LazyGetRequiredService<Volo.Abp.Timing.IClock>().Returns(clock);
        var context = Request("POST", "/Admin/ImpersonateScheduler");
        var page = new CampusFlow.Web.Pages.Admin.ImpersonateSchedulerModel(lookup, _assignments, _session, _permissions,
            NullLogger<CampusFlow.Web.Pages.Admin.ImpersonateSchedulerModel>.Instance, Substitute.For<CampusFlow.Branding.ITenantThemeProvider>())
            { Email="other@example.test", PageContext=new Microsoft.AspNetCore.Mvc.RazorPages.PageContext{HttpContext=context}, LazyServiceProvider=lazy };
        var result = await page.OnPostStartAsync();
        if (permission) Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
        else Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(result);
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }
    [Fact]
    public async Task Impersonated_responses_are_not_cached()
    {
        var context = Request(); Assert.True(await Run(context));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
    [Fact]
    public void A_claim_outside_the_protected_view_identity_does_not_change_scheduler_access()
    {
        var context = Request(); context.User.AddIdentity(new ClaimsIdentity([new Claim(SchedulerViewClaimTypes.Email, "other@example.test")], "OtherIdentity"));
        Assert.False(new CurrentSchedulerView(new HttpContextAccessor{HttpContext=context}).IsActive);
    }
}
