using System.IO;
using System.Threading.Tasks;
using CampusFlow.Web.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CampusFlow.Web.Tests.Maintenance;

public sealed class MaintenanceModeMiddlewareTests
{
    [Fact]
    public async Task Disabled_mode_should_continue_the_request()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(new MaintenanceModeOptions(), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext("/");

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabled_mode_should_return_branded_503_page()
    {
        var middleware = CreateMiddleware(new MaintenanceModeOptions
        {
            Enabled = true,
            Description = "We are moving to Elements this weekend."
        });
        var context = CreateContext("/Billing");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.ShouldBe("text/html; charset=utf-8");
        context.Response.Headers.RetryAfter.ToString().ShouldBe("300");
        var body = await ReadBodyAsync(context);
        body.ShouldContain("We are moving to Elements this weekend.");
        body.ShouldContain("Nelson University");
    }

    [Fact]
    public async Task Description_should_be_html_encoded()
    {
        var middleware = CreateMiddleware(new MaintenanceModeOptions
        {
            Enabled = true,
            Description = "Please wait <script>alert('x')</script>"
        });
        var context = CreateContext("/");

        await middleware.InvokeAsync(context);

        var body = await ReadBodyAsync(context);
        body.ShouldNotContain("<script>alert");
        body.ShouldContain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("/health-status")]
    [InlineData("/health-status/details")]
    [InlineData("/images/tenants/nelson/logo.png")]
    [InlineData("/styles/site.css")]
    public async Task Operational_and_asset_paths_should_remain_available(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(new MaintenanceModeOptions { Enabled = true }, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    private static MaintenanceModeMiddleware CreateMiddleware(
        MaintenanceModeOptions options,
        RequestDelegate? next = null)
    {
        var monitor = Substitute.For<IOptionsMonitor<MaintenanceModeOptions>>();
        monitor.CurrentValue.Returns(options);
        return new MaintenanceModeMiddleware(next ?? (_ => Task.CompletedTask), monitor);
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
