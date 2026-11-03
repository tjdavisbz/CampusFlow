using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public sealed class ThesisElementsStudentNotesServiceTests
{
    [Fact]
    public void Scheduler_comment_uses_the_selected_term_and_the_legacy_public_visibility()
    {
        var submittedAt = new DateTime(2026, 11, 3, 9, 0, 0);
        var subject = ThesisElementsStudentNotesService.BuildSchedulerSubject(" Spring 2027 ");
        var body = ThesisElementsStudentNotesService.BuildRequestBody(12345, subject,
            "Please meet with your advisor.", submittedAt);

        Assert.Equal("Scheduler Comments Spring 2027", body["subject"]!.GetValue<string>());
        Assert.Equal(12345, body["studentUID"]!.GetValue<int>());
        Assert.Equal(0, body["studentNotesID"]!.GetValue<int>());
        Assert.Equal("Public", body["viewerAccess"]!.GetValue<string>());
        Assert.Equal("Please meet with your advisor.", body["note"]!.GetValue<string>());
        Assert.Equal(0, body["transcriptPurposeID"]!.GetValue<int>());
    }

    [Fact]
    public void Retry_only_reuses_a_note_from_this_submission()
    {
        var submittedAt = new DateTime(2026, 11, 3, 9, 0, 0);
        var note = JsonNode.Parse("""
            {"studentUID":12345,"subject":"Scheduler Comments Spring 2027",
             "note":"Please meet with your advisor.","noteDate":"2026-11-03T09:00:00"}
            """);

        Assert.True(ThesisElementsStudentNotesService.IsThisSubmissionNote(note, 12345,
            "Scheduler Comments Spring 2027", "Please meet with your advisor.", submittedAt));
        Assert.False(ThesisElementsStudentNotesService.IsThisSubmissionNote(note, 12345,
            "Scheduler Comments Spring 2027", "A different message", submittedAt));
        Assert.False(ThesisElementsStudentNotesService.IsThisSubmissionNote(note, 12345,
            "Scheduler Comments Spring 2027", "Please meet with your advisor.", submittedAt.AddDays(1)));
    }

    [Fact]
    public async Task Creates_one_elements_note_and_does_not_duplicate_it_on_retry()
    {
        var handler = new NotesHandler();
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new System.Collections.Generic.Dictionary<string, string?>
            {
                ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"] = "https://elements.example",
                ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"] = "nelson.example",
                ["StudentInformationSystems:Providers:ThesisElements:Api:Username"] = "test-user",
                ["StudentInformationSystems:Providers:ThesisElements:Api:Password"] = "test-password"
            }).Build();
        var service = new ThesisElementsStudentNotesService(configuration, cache, http);
        var submittedAt = new DateTime(2026, 11, 3, 9, 0, 0);

        await service.EnsureSchedulerCommentAsync("12345", "Spring 2027", "Please meet with me.", submittedAt);
        await service.EnsureSchedulerCommentAsync("12345", "Spring 2027", "Please meet with me.", submittedAt);

        Assert.Equal(1, handler.CreateCount);
        Assert.Equal(2, handler.SearchCount);
        Assert.Equal("Scheduler Comments Spring 2027", handler.CreatedNote!["subject"]!.GetValue<string>());
    }

    private sealed class NotesHandler : HttpMessageHandler
    {
        public int CreateCount { get; private set; }
        public int SearchCount { get; private set; }
        public JsonNode? CreatedNote { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("nelson.example", request.Headers.GetValues("TenantHost").Single());
            Assert.Equal("FinancialAid", request.Headers.GetValues("Module").Single());
            var endpoint = request.RequestUri!.AbsolutePath;
            if (endpoint == "/api/Login/Authenticate")
                return Json("""{"data":{"token":"test-token"}}""");
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            if (endpoint == "/api/financial-aid/student-notes/search/12345")
            {
                SearchCount++;
                var data = new JsonArray();
                if (CreatedNote is not null) data.Add(CreatedNote.DeepClone());
                return Json(new JsonObject { ["isSuccess"] = true, ["data"] = data }.ToJsonString());
            }
            Assert.Equal("/api/financial-aid/student-notes/note", endpoint);
            Assert.Equal(HttpMethod.Post, request.Method);
            CreateCount++;
            CreatedNote = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Json("""{"isSuccess":true,"statusCode":200}""");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }
}
