using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsMealPlanService : IStudentInformationSystemMealPlanService, ITransientDependency
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TokenCacheKey = "ThesisElements:MealPlan:IntegrationToken";
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public ThesisElementsMealPlanService(IConfiguration configuration, IMemoryCache cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<StudentMealPlanContext> GetContextAsync(string externalStudentId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));

        const string sql = """
            SELECT TOP (1) COALESCE(NULLIF(LTRIM(RTRIM(AttendanceType)), ''), 'Unknown')
            FROM dbo.CAMS_Student_View WHERE StudentUID = @StudentUID;

            SELECT MealPlanID, LTRIM(RTRIM(MealPlanName)),
                   COALESCE(NULLIF(LTRIM(RTRIM(PortalMealPlanDescription)), ''),
                            NULLIF(LTRIM(RTRIM(Description)), ''), LTRIM(RTRIM(MealPlanName))),
                   Amount, StartDate, EndDate
            FROM StudentLife.MealPlans
            WHERE IsActive = 1 AND EndDate >= CAST(GETDATE() AS date)
              AND LTRIM(RTRIM(MealPlanName)) <> 'John'
            ORDER BY StartDate, MealPlanName;
            """;
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var attendanceType = await reader.ReadAsync(cancellationToken) ? reader.GetString(0) : "Unknown";
        await reader.NextResultAsync(cancellationToken);
        var plans = new List<StudentMealPlanCatalogItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(new StudentMealPlanCatalogItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : Convert.ToDecimal(reader.GetValue(3)), reader.GetDateTime(4), reader.GetDateTime(5)));
        }
        return new StudentMealPlanContext(attendanceType, plans);
    }

    public async Task AssignAsync(string externalStudentId, int mealPlanId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));
        var token = await GetTokenAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Post,
            "/api/student-life/housing/meal-plan/assign-meal-plan", token);
        request.Content = new StringContent(JsonSerializer.Serialize(new { mealPlanID = mealPlanId, studentUID = studentUid }),
            Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Elements meal-plan assignment failed ({(int)response.StatusCode}).");
        if (JsonNode.Parse(body)?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not accept the meal-plan assignment.");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Elements authentication failed.");
        var token = JsonNode.Parse(body)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements authentication returned no token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route, string token)
    {
        var tenantHost = Setting("TenantHost").TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{Setting("RegistrationBaseUrl").TrimEnd('/')}/{route.TrimStart('/')}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "StudentLife");
        request.Headers.Add("TenantHost", tenantHost);
        request.Headers.Add("Origin", $"https://{tenantHost}");
        request.Headers.Referrer = new Uri($"https://{tenantHost}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private string Setting(string name) => _configuration[$"{ApiPath}:{name}"]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private string GetConnectionString() => _configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException($"Missing connection string '{ConnectionStringName}'.");
}
