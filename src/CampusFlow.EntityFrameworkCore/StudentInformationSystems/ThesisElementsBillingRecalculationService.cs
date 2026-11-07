using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsBillingRecalculationService : IStudentInformationSystemBillingRecalculationService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private const string Prefix = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _client;
    private readonly TimeSpan _pollInterval;
    private readonly int _maximumPolls;
    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
    public ThesisElementsBillingRecalculationService(IConfiguration configuration, IMemoryCache cache) : this(configuration, cache, SharedClient) { }
    internal ThesisElementsBillingRecalculationService(IConfiguration configuration, IMemoryCache cache, HttpClient client,
        TimeSpan? pollInterval = null, int maximumPolls = 30)
    { _configuration = configuration; _cache = cache; _client = client; _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1.5); _maximumPolls = maximumPolls; }

    private sealed class RecalculationState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int? JobId;
        public bool SubmissionUnconfirmed;
    }

    public async Task RecalculateAsync(string externalStudentId, string externalTermId, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue<bool>("BillApproval:EnableBillingRecalculation"))
            throw new InvalidOperationException("Bill approval billing recalculation has not been enabled for this environment.");
        if (!int.TryParse(externalStudentId, out var student) || student <= 0 || !int.TryParse(externalTermId, out var term) || term <= 0 || operationId == Guid.Empty)
            throw new ArgumentException("Recalculation requires valid student, term and operation identifiers.");
        var integration = ValidRoot("BaseUrl");
        var gateway = ValidRoot("RegistrationBaseUrl");
        var nonprod = string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"], "Staging", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(integration.Host, nonprod ? "app-elm-nonprod-usc-integrationapi.azurewebsites.net" : "app-elm-prod-usc-integrationapi.azurewebsites.net", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(gateway.Host, nonprod ? "np-usc-gateway.thesiscloud.com" : "el-usc-gateway.thesiscloud.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Setting("TenantHost"), nonprod ? "el-np-nel.thesiscloud.com" : "el-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Billing recalculation endpoints must all belong to the configured Elements environment.");

        // Keep the exact job across page refreshes. Never resubmit an unknown POST outcome,
        // or claim another student's job is this student's completed recalculation.
        var stateKey = $"Elements:BillingRecalculationJob:{gateway.Host}:{Setting("TenantHost")}:{student}:{term}";
        RecalculationState state;
        lock (_cache)
        {
            if (!_cache.TryGetValue(stateKey, out RecalculationState? cached) || cached is null)
                _cache.Set(stateKey, cached = new RecalculationState());
            state = cached;
        }
        await state.Gate.WaitAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        try
        {
            if (state.SubmissionUnconfirmed)
                throw new InvalidOperationException("The billing recalculation submission is unconfirmed. Student Billing must reconcile it before retrying.");
            var bearer = await AuthenticateAsync(integration, token);
            if (state.JobId is null)
            {
                using var choices = Request(HttpMethod.Get, new Uri(gateway, "api/billing/authorize/names-select-items"), bearer);
                var choiceBody = await ReadAsync(choices, token);
                if (choiceBody?["data"]?["authorizeActions"]?.AsArray().Any(x => x?["uniqueId"]?.GetValue<string>() == "Recalculate") != true)
                    throw new InvalidOperationException("Elements did not offer the Recalculate billing action.");
                using var active = Request(HttpMethod.Get, new Uri(gateway, "api/billing/authorize/jobs/active"), bearer);
                var activeBody = await ReadAsync(active, token);
                if (activeBody is not JsonObject activeObject || !activeObject.ContainsKey("data") || activeObject["data"] is not null)
                    throw new InvalidOperationException("An existing Elements billing job needs to finish or be reconciled before this bill can be refreshed.");
                using var request = Request(HttpMethod.Post, new Uri(gateway, "api/billing/authorize/jobs"), bearer);
                request.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    students = new[] { new { studentUId = student, termId = term, effectiveDate = DateTime.UtcNow, action = "Recalculate", operationId } },
                    checkFinAidLoad = true
                }), Encoding.UTF8, "application/json");
                state.SubmissionUnconfirmed = true;
                using var result = await _client.SendAsync(request, token);
                if (result.StatusCode != HttpStatusCode.Accepted && result.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException($"Elements did not confirm the billing job submission (HTTP {(int)result.StatusCode}). Student Billing must reconcile it before retrying.");
                var body = JsonNode.Parse(await result.Content.ReadAsStringAsync(token));
                if (body?["isSuccess"]?.GetValue<bool>() != true || body?["data"]?["backgroundTaskId"]?.GetValue<int>() is not > 0)
                    throw new InvalidOperationException("Elements accepted the billing request without a confirmed job identifier. Approval was stopped.");
                state.JobId = body["data"]!["backgroundTaskId"]!.GetValue<int>();
                state.SubmissionUnconfirmed = false;
            }
            for (var poll = 0; poll < _maximumPolls; poll++)
            {
                using var status = Request(HttpMethod.Get, new Uri(gateway, $"api/billing/authorize/jobs/{state.JobId}"), bearer);
                var job = (await ReadAsync(status, token))?["data"];
                if (job?["backgroundTaskId"]?.GetValue<int>() != state.JobId || job?["total"]?.GetValue<int>() != 1)
                    throw new InvalidOperationException("Elements returned an unrelated or invalid billing job. Approval was stopped.");
                if (job?["status"]?.GetValue<string>() == "Completed" &&
                    job["completed"]?.GetValue<int>() == 1 && job["succeeded"]?.GetValue<int>() == 1 &&
                    job["failed"]?.GetValue<int>() == 0 && job["failedItems"] is JsonArray { Count: 0 })
                {
                    state.JobId = null; // A later review must refresh again, including any intervening course/meal changes.
                    return;
                }
                if (job?["status"]?.GetValue<string>() is not ("Pending" or "Running" or "InProgress") ||
                    job["failed"]?.GetValue<int>() != 0 || job["failedItems"] is not JsonArray { Count: 0 })
                    throw new InvalidOperationException($"Elements billing job {state.JobId} did not complete safely. Student Billing must reconcile it before retrying.");
                if (poll + 1 < _maximumPolls) await Task.Delay(_pollInterval, token);
            }
            throw new InvalidOperationException($"Elements billing job {state.JobId} is still processing. Approval must wait.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The billing refresh has not been confirmed. Approval must wait; no additional billing job was submitted.");
        }
        finally { state.Gate.Release(); }
    }

    private async Task<string> AuthenticateAsync(Uri root, CancellationToken cancellationToken)
    {
        var cacheKey = $"Elements:BillingRecalculation:{root.Host}:{Setting("TenantHost")}";
        if (_cache.TryGetValue(cacheKey, out string? token) && !string.IsNullOrWhiteSpace(token)) return token;
        using var auth = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/Login/Authenticate"));
        auth.Headers.Add("TenantHost", Setting("TenantHost"));
        auth.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await _client.SendAsync(auth, cancellationToken);
        response.EnsureSuccessStatusCode();
        token = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements billing authentication failed.");
        _cache.Set(cacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private async Task<JsonNode?> ReadAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await _client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
        if (body?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not confirm the billing job lookup. Approval was stopped.");
        return body;
    }

    private HttpRequestMessage Request(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Add("Module", "Billing");
        request.Headers.Add("Origin", $"https://{Setting("TenantHost")}");
        request.Headers.Referrer = new Uri($"https://{Setting("TenantHost")}");
        return request;
    }
    private Uri ValidRoot(string name)
    {
        var root = new Uri(Setting(name).TrimEnd('/') + "/");
        if (root.Scheme != Uri.UriSchemeHttps || !root.IsDefaultPort || root.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment) || !string.IsNullOrEmpty(root.UserInfo))
            throw new InvalidOperationException("The billing integration URL is invalid.");
        return root;
    }
    private string Setting(string name) => _configuration[Prefix + name] ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");
}
