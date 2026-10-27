using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Web.Portals;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class StudentFerpaServiceTests
{
    [Fact]
    public void Legacy_Formats_Hide_Passwords_In_List_And_Reveal_Only_Requested_Value()
    {
        var personal = new StudentFerpaEntry(1, 123, 5166, true, "Individual Privacy Password: secret-one");
        var contact = new StudentFerpaEntry(2, 123, 5166, true, "Parent: Jane, Doe - Password: secret-two");

        StudentFerpaFormatting.Describe(personal).Label.ShouldBe("Individual privacy password");
        StudentFerpaFormatting.Describe(personal).Label.ShouldNotContain("secret-one");
        StudentFerpaFormatting.Reveal(personal).ShouldBe("secret-one");
        StudentFerpaFormatting.Describe(contact).Label.ShouldBe("Parent: Jane, Doe");
        StudentFerpaFormatting.Describe(contact).Label.ShouldNotContain("secret-two");
        StudentFerpaFormatting.Reveal(contact).ShouldBe("secret-two");
        StudentFerpaFormatting.BuildContact("Parent", "Jane", "Doe", "new-secret")
            .ShouldBe("Parent: Jane, Doe - Password: new-secret");
    }

    [Fact]
    public void New_Entries_Reject_Control_Characters_And_Overlong_Values()
    {
        Should.Throw<ArgumentException>(() => StudentFerpaFormatting.BuildIndividual("\n"));
        Should.Throw<ArgumentException>(() => StudentFerpaFormatting.BuildIndividual(new string('x', 51)));
        Should.Throw<ArgumentException>(() => StudentFerpaFormatting.BuildContact("Parent:", "Jane", "Doe", "secret"));
        Should.Throw<ArgumentException>(() => StudentFerpaFormatting.BuildContact("Parent", "Jane\n", "Doe", "secret"));
    }

    [Fact]
    public async Task Only_Target_Student_Visible_Consent_Entries_Are_Returned()
    {
        var calls = 0;
        var service = CreateService(async request =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authenticate", StringComparison.Ordinal))
                return Json("""{"data":{"token":"test-token"}}""");
            request.RequestUri.AbsolutePath.ShouldBe("/api/student/student-ferpa/123");
            request.Headers.GetValues("Module").ShouldContain("Admissions");
            request.Headers.GetValues("TenantHost").ShouldContain("test-tenant.example");
            request.Headers.Authorization!.Parameter.ShouldBe("test-token");
            return Json("""
                {"isSuccess":true,"data":[
                  {"studentUid":123,"studentFerpaUid":1,"ferpaitemId":5166,"allowDisplay":true,"relationCanRecv":"Individual Privacy Password: one"},
                  {"studentUid":999,"studentFerpaUid":2,"ferpaitemId":5166,"allowDisplay":true,"relationCanRecv":"Individual Privacy Password: other"},
                  {"studentUid":123,"studentFerpaUid":3,"ferpaitemId":3333,"allowDisplay":true,"relationCanRecv":"Other item"},
                  {"studentUid":123,"studentFerpaUid":4,"ferpaitemId":5166,"allowDisplay":false,"relationCanRecv":"Hidden"}
                ]}
                """);
        });

        var entries = await service.GetAsync(123, CancellationToken.None);

        entries.Count.ShouldBe(1);
        entries[0].Id.ShouldBe(1);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Delete_Requires_Entry_Ownership_Before_Calling_Elements()
    {
        var deletes = 0;
        var service = CreateService(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authenticate", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"data":{"token":"test-token"}}"""));
            if (request.Method == HttpMethod.Delete) deletes++;
            return Task.FromResult(Json("""
                {"isSuccess":true,"data":[
                  {"studentUid":123,"studentFerpaUid":10,"ferpaitemId":5166,"allowDisplay":true,"relationCanRecv":"Individual Privacy Password: one"}
                ]}
                """));
        });

        await Should.ThrowAsync<InvalidOperationException>(() => service.DeleteAsync(123, 11, CancellationToken.None));
        deletes.ShouldBe(0);
        await service.DeleteAsync(123, 10, CancellationToken.None);
        deletes.ShouldBe(1);
    }

    [Fact]
    public async Task Add_Uses_Elements_Consent_Item_And_Current_Student_Id()
    {
        string? posted = null;
        var service = CreateService(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authenticate", StringComparison.Ordinal))
                return Json("""{"data":{"token":"test-token"}}""");
            if (request.Method == HttpMethod.Post)
            {
                posted = await request.Content!.ReadAsStringAsync();
                return Json("""{"isSuccess":true}""");
            }
            return Json("""{"isSuccess":true,"data":[]}""");
        });

        await service.AddContactAsync(123, "Parent", "Jane", "Doe", "secret", CancellationToken.None);

        using var document = JsonDocument.Parse(posted!);
        var root = document.RootElement;
        root.GetProperty("studentUid").GetInt32().ShouldBe(123);
        root.GetProperty("ferpaitemId").GetInt32().ShouldBe(5166);
        root.GetProperty("allowDisplay").GetBoolean().ShouldBeTrue();
        root.GetProperty("relationCanRecv").GetString().ShouldBe("Parent: Jane, Doe - Password: secret");
    }

    private static StudentFerpaService CreateService(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] = "https://integration.example",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"] = "https://gateway.example",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "test-tenant.example",
            ["StudentInformationSystems:Providers:ThesisElements:Api:Username"] = "test-user",
            ["StudentInformationSystems:Providers:ThesisElements:Api:Password"] = "test-password"
        }).Build();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new StubHandler(respond)));
        return new StudentFerpaService(config, factory, new MemoryCache(new MemoryCacheOptions()));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
