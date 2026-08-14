using System.Threading.Tasks;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class DevelopmentPortalContextMiddlewareTests
{
    [Theory]
    [InlineData("student", PortalType.Student)]
    [InlineData("admin", PortalType.Admin)]
    [InlineData("advisor", PortalType.Advisor)]
    [InlineData("scheduler", PortalType.Advisor)]
    public async Task Should_Resolve_Development_Tenant_And_Portal(
        string portal,
        PortalType expectedPortal)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?tenant=nelson&portal={portal}");
        var middleware = new DevelopmentPortalContextMiddleware(_ => Task.CompletedTask, environment);

        await middleware.InvokeAsync(context);

        context.Request.Query["__tenant"].ToString().ShouldBe("nelson");
        context.Items[DevelopmentPortalContextMiddleware.PortalItemKey].ShouldBe(expectedPortal);
    }

    [Fact]
    public async Task Should_Ignore_Overrides_Outside_Development()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenant=nelson&portal=student");
        var middleware = new DevelopmentPortalContextMiddleware(_ => Task.CompletedTask, environment);

        await middleware.InvokeAsync(context);

        context.Request.Query.ContainsKey("__tenant").ShouldBeFalse();
        context.Items.ContainsKey(DevelopmentPortalContextMiddleware.PortalItemKey).ShouldBeFalse();
    }
}
