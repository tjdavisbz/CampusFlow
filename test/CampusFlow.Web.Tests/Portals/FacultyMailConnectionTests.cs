using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class FacultyMailConnectionTests
{
    [Fact]
    public async Task Authorization_Is_Persisted_Encrypted_And_Bound_To_The_Linked_User()
    {
        var store = new MemoryTokenStore();
        var actor = Guid.NewGuid();
        var connection = CreateConnection(store);
        await connection.ConnectAsync(actor, "tenant:account", "faculty@nelson.edu", "access-secret",
            "refresh-secret", DateTimeOffset.UtcNow.AddMinutes(30));

        store.Value.ShouldNotBeNull();
        store.Value.ShouldNotContain("refresh-secret");
        (await connection.GetAccessAsync(actor, "tenant:account", CancellationToken.None))!
            .AccessToken.ShouldBe("access-secret");
        (await connection.GetAccessAsync(Guid.NewGuid(), "tenant:account", CancellationToken.None)).ShouldBeNull();
        (await connection.GetAccessAsync(actor, "tenant:other", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Expiring_Access_Is_Renewed_Without_Faculty_Interaction()
    {
        var store = new MemoryTokenStore();
        var actor = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var requestCount = 0;
        var handler = new StubHandler(async request =>
        {
            requestCount++;
            request.RequestUri!.ToString().ShouldBe(
                $"https://login.microsoftonline.com/{tenant:D}/oauth2/v2.0/token");
            var form = await request.Content!.ReadAsStringAsync();
            form.ShouldContain("grant_type=refresh_token");
            form.ShouldContain("refresh_token=old-refresh");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"renewed-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}",
                    Encoding.UTF8, "application/json")
            };
        });
        var connection = CreateConnection(store, handler);
        await connection.ConnectAsync(actor, $"{tenant:D}:account", "faculty@nelson.edu", "old-access",
            "old-refresh", DateTimeOffset.UtcNow.AddMinutes(2));

        (await connection.GetAccessAsync(actor, $"{tenant:D}:account", CancellationToken.None))!
            .AccessToken.ShouldBe("renewed-access");
        (await connection.GetAccessAsync(actor, $"{tenant:D}:account", CancellationToken.None))!
            .AccessToken.ShouldBe("renewed-access");
        requestCount.ShouldBe(1);
    }

    private static FacultyMailConnection CreateConnection(MemoryTokenStore store, HttpMessageHandler? handler = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MicrosoftIdentity:ClientId"] = "test-client",
            ["MicrosoftIdentity:ClientSecret"] = "test-secret"
        }).Build();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler ?? new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)))));
        return new FacultyMailConnection(new EphemeralDataProtectionProvider(), store, configuration, factory,
            Substitute.For<ILogger<FacultyMailConnection>>());
    }

    private sealed class MemoryTokenStore : IFacultyMailTokenStore
    {
        public string? Value { get; private set; }
        public Task<string?> GetAsync(Guid userId) => Task.FromResult(Value);
        public Task SetAsync(Guid userId, string protectedTicket)
        {
            Value = protectedTicket;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
