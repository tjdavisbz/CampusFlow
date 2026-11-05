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
    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
    public ThesisElementsBillingRecalculationService(IConfiguration configuration, IMemoryCache cache) : this(configuration, cache, SharedClient) { }
    internal ThesisElementsBillingRecalculationService(IConfiguration configuration, IMemoryCache cache, HttpClient client)
    { _configuration = configuration; _cache = cache; _client = client; }

    public async Task RecalculateAsync(string externalStudentId, string externalTermId, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue<bool>("BillApproval:EnableBillingRecalculation"))
            throw new InvalidOperationException("Bill approval billing recalculation has not been enabled for this environment.");
        if (!int.TryParse(externalStudentId, out var student) || student <= 0 || !int.TryParse(externalTermId, out var term) || term <= 0 || operationId == Guid.Empty)
            throw new ArgumentException("Recalculation requires valid student, term and operation identifiers.");
        var root = new Uri(Setting("BaseUrl").TrimEnd('/') + "/");
        if (root.Scheme != Uri.UriSchemeHttps || !root.IsDefaultPort || root.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment) || !string.IsNullOrEmpty(root.UserInfo))
            throw new InvalidOperationException("The billing integration URL is invalid.");
        if ((string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"], "Staging", StringComparison.OrdinalIgnoreCase)) &&
            (!string.Equals(root.Host, "app-elm-nonprod-usc-integrationapi.azurewebsites.net", StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(Setting("TenantHost"), "el-np-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Non-production billing recalculation cannot target production Elements.");
        var cacheKey = $"Elements:BillingRecalculation:{root.Host}:{Setting("TenantHost")}";
        if (!_cache.TryGetValue(cacheKey, out string? token) || string.IsNullOrWhiteSpace(token))
        {
            using var auth = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/Login/Authenticate"));
            auth.Headers.Add("TenantHost", Setting("TenantHost"));
            auth.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
            using var response = await _client.SendAsync(auth, cancellationToken);
            response.EnsureSuccessStatusCode();
            token = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?["token"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements billing authentication failed.");
            _cache.Set(cacheKey, token, TimeSpan.FromMinutes(10));
        }
        using var choices = Request(HttpMethod.Get, new Uri(root, "api/billing/authorize/names-select-items"), token);
        using var choiceResponse = await _client.SendAsync(choices, cancellationToken);
        choiceResponse.EnsureSuccessStatusCode();
        var choiceBody = JsonNode.Parse(await choiceResponse.Content.ReadAsStringAsync(cancellationToken));
        if (choiceBody?["isSuccess"]?.GetValue<bool>() != true ||
            choiceBody?["data"]?["authorizeActions"]?.AsArray().Any(x => x?["uniqueId"]?.GetValue<string>() == "Recalculate") != true)
            throw new InvalidOperationException("Elements did not offer the Recalculate billing action.");
        using var request = Request(HttpMethod.Post, new Uri(root, "api/billing/authorize/authorize-student"), token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        { studentUId = student, termId = term, effectiveDate = DateTime.UtcNow, action = "Recalculate", checkFinAidLoad = true, operationId }), Encoding.UTF8, "application/json");
        using var result = await _client.SendAsync(request, cancellationToken);
        if (result.StatusCode == HttpStatusCode.Accepted)
            throw new InvalidOperationException("Elements is still processing the billing recalculation. Approval must wait.");
        result.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await result.Content.ReadAsStringAsync(cancellationToken));
        if (body?["statusCode"]?.GetValue<int>() == 202 || body?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not confirm billing recalculation. Approval was stopped.");
    }

    private HttpRequestMessage Request(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("TenantHost", Setting("TenantHost"));request.Headers.Add("Module", "Billing");
        return request;
    }
    private string Setting(string name) => _configuration[Prefix + name] ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");
}
