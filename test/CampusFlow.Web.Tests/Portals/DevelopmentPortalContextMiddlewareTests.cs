using System.Threading.Tasks;
using CampusFlow.Portals;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
        var middleware = CreateMiddleware(environment);

        await middleware.InvokeAsync(context);

        context.Request.Query["__tenant"].ToString().ShouldBe("nelson");
        context.Items[DevelopmentPortalContextMiddleware.PortalItemKey].ShouldBe(expectedPortal);
    }

    [Fact]
    public async Task Should_Use_Configured_Tenant_And_Ignore_Portal_Overrides_Outside_Development()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenant=nelson&portal=student");
        var middleware = CreateMiddleware(environment);

        await middleware.InvokeAsync(context);

        context.Request.Query["__tenant"].ToString().ShouldBe("nelson");
        context.Items.ContainsKey(DevelopmentPortalContextMiddleware.PortalItemKey).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Use_Configured_Default_Tenant_In_Development()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(environment);

        await middleware.InvokeAsync(context);

        context.Request.Query["__tenant"].ToString().ShouldBe("nelson");
    }

    [Fact]
    public async Task Should_Preserve_Abp_Tenant_Query_Parameter()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?__tenant=nelson");
        var middleware = CreateMiddleware(environment);

        await middleware.InvokeAsync(context);

        context.Request.Query["__tenant"].ToString().ShouldBe("nelson");
    }

    private static DevelopmentPortalContextMiddleware CreateMiddleware(IWebHostEnvironment environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DevelopmentPortal:DefaultTenant"] = "nelson",
                ["TenantResolution:DefaultTenant"] = "nelson"
            })
            .Build();

        return new DevelopmentPortalContextMiddleware(_ => Task.CompletedTask, environment, configuration);
    }
}
