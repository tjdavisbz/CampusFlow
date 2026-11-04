using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsStudentNotesService : IStudentInformationSystemStudentNotesService
{
    private const string ConfigurationPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TokenCacheKey = "ThesisElements:StudentNotes:ApiToken";
    private static readonly HttpClient SharedHttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;

    public ThesisElementsStudentNotesService(IConfiguration configuration, IMemoryCache cache)
        : this(configuration, cache, SharedHttpClient)
    {
    }

    internal ThesisElementsStudentNotesService(IConfiguration configuration, IMemoryCache cache,
        HttpClient httpClient)
    {
        _configuration = configuration;
        _cache = cache;
        _httpClient = httpClient;
    }

    public async Task EnsureSchedulerCommentAsync(string externalStudentId, string termName, string comment,
        DateTime submittedAt, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, NumberStyles.None, CultureInfo.InvariantCulture, out var studentUid) ||
            studentUid <= 0)
            throw new ArgumentException("The Elements student UID is invalid.", nameof(externalStudentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(termName);
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        var subject = BuildSchedulerSubject(termName);
        var body = comment.Trim();
        var token = await GetAccessTokenAsync(cancellationToken);

        // A retry after an uncertain API or database result must not create a second note.
        using (var search = CreateRequest(HttpMethod.Get,
                   $"api/financial-aid/student-notes/search/{studentUid}", token))
        using (var response = await _httpClient.SendAsync(search, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.NoContent)
            {
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonNode.Parse(content);
                if (result?["isSuccess"]?.GetValue<bool>() != true)
                    throw new InvalidOperationException("Elements could not check existing Student Notes.");
                if (result["data"]?.AsArray().Any(note => IsThisSubmissionNote(note, studentUid,
                        subject, body, submittedAt)) == true)
                    return;
            }
        }

        var requestBody = BuildRequestBody(studentUid, subject, body, submittedAt);
        using var create = CreateRequest(HttpMethod.Post, "api/financial-aid/student-notes/note", token);
        create.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var created = await _httpClient.SendAsync(create, cancellationToken);
        var createdContent = await created.Content.ReadAsStringAsync(cancellationToken);
        created.EnsureSuccessStatusCode();
        if (JsonNode.Parse(createdContent)?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not accept the Student Note.");
    }

    internal static string BuildSchedulerSubject(string termName) => $"Scheduler Comments {termName.Trim()}";

    internal static JsonObject BuildRequestBody(int studentUid, string subject, string comment, DateTime submittedAt) =>
        new()
        {
            ["studentNotesID"] = 0,
            ["studentUID"] = studentUid,
            ["noteDate"] = submittedAt.ToString("O", CultureInfo.InvariantCulture),
            ["viewerAccess"] = "Public",
            ["subject"] = subject,
            ["note"] = comment,
            ["groupAccess"] = "",
            ["facultyID"] = null,
            ["transcriptPurposeID"] = 0
        };

    internal static bool IsThisSubmissionNote(JsonNode? note, int studentUid, string subject,
        string body, DateTime submittedAt)
    {
        if (note is null || note["studentUID"]?.GetValue<int>() != studentUid ||
            !string.Equals(note["subject"]?.GetValue<string>(), subject, StringComparison.Ordinal) ||
            !string.Equals(note["note"]?.GetValue<string>(), body, StringComparison.Ordinal))
            return false;
        return DateTime.TryParse(note["noteDate"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var noteDate) &&
               Math.Abs((noteDate - submittedAt).TotalMinutes) <= 1;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;
        using var request = CreateRequest(HttpMethod.Get, "api/Login/Authenticate");
        var credentials = $"{RequiredSetting("Username")}:{RequiredSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Elements authentication did not return a token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? token = null)
    {
        var request = new HttpRequestMessage(method,
            $"{RequiredSetting("BaseUrl").TrimEnd('/')}/{relativeUrl.TrimStart('/')}");
        request.Headers.Add("TenantHost", RequiredSetting("TenantHost"));
        request.Headers.Add("Module", "FinancialAid");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private string RequiredSetting(string name) => _configuration[$"{ConfigurationPath}:{name}"] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.");
}
