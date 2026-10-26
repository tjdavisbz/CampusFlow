using System.Threading.Tasks;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class LegacyPortalHomeRedirectMiddlewareTests
{
    [Theory]
    [InlineData("GET", "/index.asp")]
    [InlineData("GET", "/INDEX.ASP")]
    [InlineData("HEAD", "/index.asp")]
    public async Task Legacy_Home_Should_Redirect_Without_Old_Query_Parameters(string method, string path)
    {
        var continued = false;
        var middleware = new LegacyPortalHomeRedirectMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?oldSession=secret");

        await middleware.InvokeAsync(context);

        continued.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().ShouldBe("/");
    }

    [Theory]
    [InlineData("POST", "/index.asp")]
    [InlineData("GET", "/other.asp")]
    public async Task Other_Requests_Should_Continue(string method, string path)
    {
        var continued = false;
        var middleware = new LegacyPortalHomeRedirectMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        continued.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }
}
