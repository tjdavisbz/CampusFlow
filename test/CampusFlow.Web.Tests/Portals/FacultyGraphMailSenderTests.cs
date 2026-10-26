using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Web.Portals;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class FacultyGraphMailSenderTests
{
    [Fact]
    public async Task Sends_As_Signed_In_User_With_Students_In_Bcc_And_Sent_Items()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            request.RequestUri!.ToString().ShouldBe("https://graph.microsoft.com/v1.0/me/sendMail");
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        var sender = new FacultyGraphMailSender(factory, Substitute.For<ILogger<FacultyGraphMailSender>>());

        var result = await sender.SendAsync("test-token", "professor@nelson.edu", "Course news", "Hello class",
            ["one@my.nelson.edu", "two@my.nelson.edu"], CancellationToken.None);

        result.Accepted.ShouldBeTrue();
        using var json = JsonDocument.Parse(requestBody!);
        var message = json.RootElement.GetProperty("message");
        message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address")
            .GetString().ShouldBe("professor@nelson.edu");
        message.GetProperty("bccRecipients").EnumerateArray().Count().ShouldBe(2);
        message.GetProperty("body").GetProperty("contentType").GetString().ShouldBe("Text");
        json.RootElement.GetProperty("saveToSentItems").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Does_Not_Call_Graph_For_Too_Many_Recipients()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var sender = new FacultyGraphMailSender(factory, Substitute.For<ILogger<FacultyGraphMailSender>>());
        var result = await sender.SendAsync("test-token", "professor@nelson.edu", "Subject", "Body",
            Enumerable.Range(1, 101).Select(x => $"student{x}@my.nelson.edu").ToArray(), CancellationToken.None);
        result.Accepted.ShouldBeFalse();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task Denied_Mail_Permission_Is_Reported_Without_Claiming_Delivery()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))));
        var sender = new FacultyGraphMailSender(factory, Substitute.For<ILogger<FacultyGraphMailSender>>());

        var result = await sender.SendAsync("test-token", "professor@nelson.edu", "Subject", "Body",
            ["student@my.nelson.edu"], CancellationToken.None);

        result.Accepted.ShouldBeFalse();
        result.AuthorizationFailed.ShouldBeTrue();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
