using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsFinancialAidDecisionService : IStudentInformationSystemFinancialAidDecisionService
{
    private const string ConfigurationPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TokenCacheKey = "ThesisElements:FinancialAid:ApiToken";
    private static readonly string[] RequestProperties =
    [
        "financialStatusId", "studentUid", "termCalendarId", "transDate", "transTypeId",
        "transStatusId", "amount", "estimatedAmount", "description", "referenceNo",
        "creditStatusId", "code1Id", "code2Id", "showOnBillingStatement", "dateCheckSigned",
        "requiredCredits", "studentAccepted", "transId", "lock", "campusId",
        "paymentPeriodStartDate", "paymentPeriodEndDate", "codaei", "originationFeeAmount",
        "interestRebateAmount", "calendarYear", "programAttendanceBeginDate",
        "workForcePellTuitionAndFees", "institutionalLimitApplied"
    ];

    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public ThesisElementsFinancialAidDecisionService(IConfiguration configuration, IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<IReadOnlyDictionary<string, int?>> GetDecisionsAsync(
        string externalStudentId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
        {
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        var awards = await GetStudentAwardsAsync(studentUid, token, cancellationToken);
        var decisions = new Dictionary<string, int?>();
        foreach (var node in awards)
        {
            var award = node?.AsObject();
            if (award?["financialAwardId"] is null)
            {
                continue;
            }

            decisions[award["financialAwardId"]!.GetValue<int>().ToString()] =
                ReadDecision(award["studentAccepted"]);
        }
        return decisions;
    }

    public async Task SubmitDecisionAsync(string externalStudentId, string externalAwardId,
        StudentFinancialAidDecision decision, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid) || !int.TryParse(externalAwardId, out var awardId))
        {
            throw new ArgumentException("The Thesis Elements student or award identifier is invalid.");
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        var award = await GetStudentAwardAsync(studentUid, awardId, token, cancellationToken);
        if (HasRecordedDecision(award["studentAccepted"]))
        {
            throw new InvalidOperationException("This award already has a recorded decision.");
        }

        var body = new JsonObject();
        foreach (var propertyName in RequestProperties)
        {
            if (award.TryGetPropertyValue(propertyName, out var value))
            {
                body[propertyName] = value?.DeepClone();
            }
        }
        body["studentAccepted"] = (int)decision;

        using var request = CreateRequest(HttpMethod.Put,
            $"api/financial-aid/maintenance/awards/financial-awards/{awardId}", token);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(content)?["isSuccess"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException("Elements did not accept the financial aid decision.");
        }
    }

    private async Task<JsonObject> GetStudentAwardAsync(int studentUid, int awardId, string token,
        CancellationToken cancellationToken)
    {
        var awards = await GetStudentAwardsAsync(studentUid, token, cancellationToken);

        foreach (var node in awards)
        {
            var award = node?.AsObject();
            if (award?["financialAwardId"]?.GetValue<int>() == awardId &&
                award["studentUid"]?.GetValue<int>() == studentUid)
            {
                return award;
            }
        }
        throw new InvalidOperationException("The selected award was not found for this student.");
    }

    private static int? ReadDecision(JsonNode? value)
    {
        if (value is not JsonValue jsonValue)
        {
            return null;
        }
        if (jsonValue.TryGetValue<bool>(out var booleanValue))
        {
            return booleanValue ? 1 : 0;
        }
        if (jsonValue.TryGetValue<int>(out var integerValue))
        {
            return integerValue is 0 or 1 ? integerValue : null;
        }
        return null;
    }

    private async Task<JsonArray> GetStudentAwardsAsync(
        int studentUid, string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"api/financial-aid/maintenance/awards/financial-awards/{studentUid}", token);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(content)?["data"]?["getFinancialAwardDetails"]?.AsArray()
            ?? throw new InvalidOperationException("Elements did not return the student's awards.");
    }

    private static bool HasRecordedDecision(JsonNode? value)
    {
        return ReadDecision(value) is not null;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        using var request = CreateRequest(HttpMethod.Get, "api/Login/Authenticate");
        var credentials = $"{GetRequiredSetting("Username")}:{GetRequiredSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Elements authentication did not return a token.");
        }
        _memoryCache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? token = null)
    {
        var request = new HttpRequestMessage(method,
            $"{GetRequiredSetting("BaseUrl").TrimEnd('/')}/{relativeUrl.TrimStart('/')}");
        request.Headers.Add("TenantHost", GetRequiredSetting("TenantHost"));
        request.Headers.Add("Module", GetRequiredSetting("Module"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    private string GetRequiredSetting(string name)
    {
        var value = _configuration[$"{ConfigurationPath}:{name}"];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.")
            : value;
    }
}
