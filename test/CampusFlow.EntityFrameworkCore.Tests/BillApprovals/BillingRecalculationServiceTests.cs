using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

public class BillingRecalculationServiceTests
{
    private static string Job(string status = "Completed", int total = 1, int completed = 1, int succeeded = 1,
        int failed = 0, int id = 1014, string failedItems = "[]") =>
        System.Text.Json.JsonSerializer.Serialize(new { isSuccess = true, data = new {
            backgroundTaskId = id, status, total, completed, succeeded, failed,
            failedItems = JsonNode.Parse(failedItems) } });

    private sealed class Handler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = [];
        public JsonNode? Payload { get; private set; }
        public bool OfferRecalculate { get; set; } = true;
        public string Active { get; set; } = "{\"isSuccess\":true,\"data\":null}";
        public HttpStatusCode StartStatus { get; set; } = HttpStatusCode.Accepted;
        public string StartBody { get; set; } = Job("Pending", completed: 0, succeeded: 0);
        public Queue<string> Polls { get; } = new();
        public bool LoseStartResponse { get; set; }
        public bool Production { get; set; }
        public int Posts => Calls.Count(x => x.Method == HttpMethod.Post);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            var tenant = Production ? "el-nel.thesiscloud.com" : "el-np-nel.thesiscloud.com";
            Assert.Equal(tenant, request.Headers.GetValues("TenantHost").Single());
            if (request.RequestUri.AbsolutePath.EndsWith("Authenticate"))
            {
                Assert.Equal(Production ? "app-elm-prod-usc-integrationapi.azurewebsites.net" : "app-elm-nonprod-usc-integrationapi.azurewebsites.net", request.RequestUri.Host);
                return Response(HttpStatusCode.OK, "{\"data\":{\"token\":\"fake-token\"}}");
            }
            Assert.Equal(Production ? "el-usc-gateway.thesiscloud.com" : "np-usc-gateway.thesiscloud.com", request.RequestUri.Host);
            Assert.Equal("Billing", request.Headers.GetValues("Module").Single());
            Assert.Equal($"https://{tenant}", request.Headers.GetValues("Origin").Single());
            Assert.Equal($"https://{tenant}/", request.Headers.Referrer!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            if (request.RequestUri.AbsolutePath.EndsWith("names-select-items"))
                return Response(HttpStatusCode.OK, OfferRecalculate ? "{\"isSuccess\":true,\"data\":{\"authorizeActions\":[{\"uniqueId\":\"Recalculate\"}]}}" : "{\"isSuccess\":true,\"data\":{\"authorizeActions\":[]}}");
            if (request.RequestUri.AbsolutePath.EndsWith("jobs/active")) return Response(HttpStatusCode.OK, Active);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/api/billing/authorize/jobs", request.RequestUri.AbsolutePath);
                Payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (LoseStartResponse) throw new HttpRequestException("Response lost after submission");
                return Response(StartStatus, StartBody);
            }
            Assert.Equal("/api/billing/authorize/jobs/1014", request.RequestUri.AbsolutePath);
            return Response(HttpStatusCode.OK, Polls.Count > 0 ? Polls.Dequeue() : Job());
        }
        private static HttpResponseMessage Response(HttpStatusCode status, string content) =>
            new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
    private static ThesisElementsBillingRecalculationService Service(Handler handler, bool enabled = true,
        Dictionary<string, string?>? overrides = null, IMemoryCache? cache = null, int maximumPolls = 2)
    {
        const string p = "StudentInformationSystems:Providers:ThesisElements:Api:";
        var values = new Dictionary<string, string?>
        {
            ["App:EnvironmentLabel"] = handler.Production ? "" : "NON-PRODUCTION",
            ["BillApproval:EnableBillingRecalculation"] = enabled.ToString(),
            [p + "BaseUrl"] = handler.Production ? "https://app-elm-prod-usc-integrationapi.azurewebsites.net" : "https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            [p + "RegistrationBaseUrl"] = handler.Production ? "https://el-usc-gateway.thesiscloud.com" : "https://np-usc-gateway.thesiscloud.com",
            [p + "TenantHost"] = handler.Production ? "el-nel.thesiscloud.com" : "el-np-nel.thesiscloud.com",
            [p + "Username"] = "fake", [p + "Password"] = "fake"
        };
        foreach (var value in overrides ?? []) values[value.Key] = value.Value;
        return new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), cache ?? new MemoryCache(new MemoryCacheOptions()),
            new HttpClient(handler), TimeSpan.Zero, maximumPolls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uses_gateway_job_and_requires_exact_student_term_operation_completion(bool production)
    {
        var handler = new Handler { Production = production };
        var id = Guid.NewGuid();
        await Service(handler).RecalculateAsync("10", "269", id);
        var student = Assert.Single(handler.Payload!["students"]!.AsArray())!;
        Assert.Equal("Recalculate", student["action"]!.GetValue<string>());
        Assert.Equal(10, student["studentUId"]!.GetValue<int>());
        Assert.Equal(269, student["termId"]!.GetValue<int>());
        Assert.Equal(id, student["operationId"]!.GetValue<Guid>());
        Assert.True(handler.Payload["checkFinAidLoad"]!.GetValue<bool>());
        Assert.Null(handler.Payload["gatewayHost"]);
        Assert.Equal(1, handler.Posts);
        Assert.DoesNotContain(handler.Calls, x => x.Path.EndsWith("authorize-student"));
        Assert.Equal("/api/billing/authorize/jobs/1014", handler.Calls.Last().Path);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("InProgress")]
    public async Task Pending_is_polled_until_confirmed_and_never_counts_as_success_by_itself(string status)
    {
        var handler = new Handler();
        handler.Polls.Enqueue(Job(status, completed: 0, succeeded: 0));
        handler.Polls.Enqueue(Job());
        await Service(handler).RecalculateAsync("10", "269", Guid.NewGuid());
        Assert.Equal(1, handler.Posts);
        Assert.Equal(2, handler.Calls.Count(x => x.Path.EndsWith("/1014")));
    }

    [Fact]
    public async Task A_page_retry_resumes_the_same_pending_job_even_with_a_new_service_and_operation_id()
    {
        var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        handler.Polls.Enqueue(Job("Pending", completed: 0, succeeded: 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler, cache: cache, maximumPolls: 1).RecalculateAsync("10", "269", Guid.NewGuid()));
        await Service(handler, cache: cache).RecalculateAsync("10", "269", Guid.NewGuid());
        Assert.Equal(1, handler.Posts);
    }

    [Theory]
    [InlineData("Failed", 1, 1, 0, 1, 1014, "[]")]
    [InlineData("Interrupted", 1, 0, 0, 0, 1014, "[]")]
    [InlineData("Completed", 2, 2, 2, 0, 1014, "[]")]
    [InlineData("Completed", 1, 0, 1, 0, 1014, "[]")]
    [InlineData("Completed", 1, 1, 0, 0, 1014, "[]")]
    [InlineData("Completed", 1, 1, 1, 1, 1014, "[]")]
    [InlineData("Completed", 1, 1, 1, 0, 9999, "[]")]
    [InlineData("Completed", 1, 1, 1, 0, 1014, "[{\"error\":\"partial\"}]")]
    [InlineData("Completed", 1, 1, 1, 0, 1014, "null")]
    public async Task Failed_incomplete_unrelated_or_inconsistent_jobs_stop_approval_and_are_not_resubmitted(
        string status, int total, int completed, int succeeded, int failed, int jobId, string errors)
    {
        var handler = new Handler();
        var result = Job(status, total, completed, succeeded, failed, jobId, errors);
        handler.Polls.Enqueue(result); handler.Polls.Enqueue(result);
        var service = Service(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecalculateAsync("10", "269", Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Another_active_job_cannot_be_adopted_or_overwritten()
    {
        var handler = new Handler { Active = Job("Running", total: 20, completed: 0, succeeded: 0) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler).RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Equal(0, handler.Posts);
    }

    [Fact]
    public async Task Missing_active_job_data_cannot_be_treated_as_no_active_job()
    {
        var handler = new Handler { Active = "{\"isSuccess\":true}" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler).RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Equal(0, handler.Posts);
    }

    [Theory]
    [InlineData(409, true, false)]
    [InlineData(400, false, false)]
    [InlineData(202, false, false)]
    [InlineData(202, true, true)]
    public async Task Unconfirmed_submissions_are_never_retried_or_adopted(int status, bool validId, bool loseResponse)
    {
        var handler = new Handler { StartStatus = (HttpStatusCode)status, StartBody = validId ? Job() : "{\"isSuccess\":true,\"data\":null}", LoseStartResponse = loseResponse };
        var service = Service(handler);
        await Assert.ThrowsAnyAsync<Exception>(() => service.RecalculateAsync("10", "269", Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Missing_Recalculate_action_prevents_any_billing_write()
    {
        var handler = new Handler { OfferRecalculate = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler).RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Equal(0, handler.Posts);
    }

    [Fact]
    public async Task A_later_review_refreshes_again_after_a_confirmed_completion()
    {
        var handler = new Handler(); var service = Service(handler);
        await service.RecalculateAsync("10", "269", Guid.NewGuid());
        await service.RecalculateAsync("10", "269", Guid.NewGuid());
        Assert.Equal(2, handler.Posts);
    }

    [Fact]
    public async Task Disabled_configuration_prevents_all_requests()
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler, enabled: false).RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("BaseUrl", "https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("RegistrationBaseUrl", "https://el-usc-gateway.thesiscloud.com")]
    [InlineData("TenantHost", "el-nel.thesiscloud.com")]
    [InlineData("RegistrationBaseUrl", "https://np-usc-gateway.thesiscloud.com/other")]
    [InlineData("RegistrationBaseUrl", "http://np-usc-gateway.thesiscloud.com")]
    public async Task Mixed_environment_or_invalid_gateway_configuration_prevents_all_requests(string name, string value)
    {
        var handler = new Handler();
        var overrides = new Dictionary<string, string?> { ["StudentInformationSystems:Providers:ThesisElements:Api:" + name] = value };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(handler, overrides: overrides).RecalculateAsync("10", "269", Guid.NewGuid()));
        Assert.Empty(handler.Calls);
    }
}
