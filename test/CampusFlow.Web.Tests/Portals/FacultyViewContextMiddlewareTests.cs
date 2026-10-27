using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.Portals;

public class FacultyViewContextMiddlewareTests
{
    [Fact]
    public async Task Faculty_View_Should_End_When_Admin_Permission_Is_Revoked()
    {
        var actorId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(actorId);
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(Guid.NewGuid());
        var session = new FacultyViewSession(new EphemeralDataProtectionProvider(), currentUser, currentTenant);
        var startContext = new DefaultHttpContext();
        session.Start(startContext, new FacultyIdentity(123, "Faculty Member"));

        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = startContext.Response.Headers.SetCookie.ToString().Split(';')[0];
        var permissionChecker = Substitute.For<IPermissionChecker>();
        permissionChecker.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default)
            .Returns(Task.FromResult(true));
        permissionChecker.IsGrantedAsync(CampusFlowPermissions.FacultyImpersonation.Default)
            .Returns(Task.FromResult(false));
        var continued = false;
        var middleware = new FacultyViewContextMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, session, permissionChecker,
            Substitute.For<ILogger<FacultyViewContextMiddleware>>());

        continued.ShouldBeTrue();
        FacultyViewSession.GetFacultyId(context).ShouldBeNull();
        context.Response.Headers.SetCookie.ToString().ShouldContain(FacultyViewSession.CookieName);
    }

    [Theory]
    [InlineData("GET", "/Faculty", true)]
    [InlineData("POST", "/Faculty", false)]
    [InlineData("PUT", "/api/something", false)]
    [InlineData("POST", "/Admin/ImpersonateFaculty/End", true)]
    public async Task Faculty_View_Should_Only_Allow_Reads_And_Exit(
        string method, string path, bool shouldContinue)
    {
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(actorId);
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(tenantId);
        var session = new FacultyViewSession(new EphemeralDataProtectionProvider(), currentUser, currentTenant);

        var startContext = new DefaultHttpContext();
        session.Start(startContext, new FacultyIdentity(123, "Faculty Member"));
        var cookie = startContext.Response.Headers.SetCookie.ToString().Split(';')[0];

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Cookie = cookie;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,
            actorId.ToString())], "Test"));

        var permissionChecker = Substitute.For<IPermissionChecker>();
        permissionChecker.IsGrantedAsync(CampusFlowPermissions.FacultyImpersonation.Default)
            .Returns(Task.FromResult(true));
        var logger = Substitute.For<ILogger<FacultyViewContextMiddleware>>();
        var continued = false;
        var middleware = new FacultyViewContextMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, session, permissionChecker, logger);

        continued.ShouldBe(shouldContinue);
        context.Response.StatusCode.ShouldBe(shouldContinue ? 200 : 403);
        FacultyViewSession.GetFacultyId(context).ShouldBe(123);
    }
}
