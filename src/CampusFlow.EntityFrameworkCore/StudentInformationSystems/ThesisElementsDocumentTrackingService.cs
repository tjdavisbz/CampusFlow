using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsDocumentTrackingService : IStudentInformationSystemDocumentTrackingService
{
    private const string ApiConfigurationPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TrackingConfigurationPath = "BillApproval:DocumentTracking";
    private const string TokenCacheKey = "ThesisElements:DocumentTracking:ApiToken";
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public ThesisElementsDocumentTrackingService(IConfiguration configuration, IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<string> CreateApprovedBillAsync(
        StudentDocumentTrackingRequest request, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (!int.TryParse(request.ExternalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(request));

        var reference = request.ApprovalId.ToString("N");
        if (await FindDocumentIdAsync(studentUid, reference, cancellationToken) is { } existingId)
            return existingId.ToString(CultureInfo.InvariantCulture);

        var token = await GetAccessTokenAsync(cancellationToken);
        var description = GetTrackingSetting("DescriptionFormat", "Approved Bill - {TermName}")
            .Replace("{TermName}", request.TermName, StringComparison.Ordinal);
        var acceptedAt = request.AcceptedAt.ToString("O", CultureInfo.InvariantCulture);
        var body = new JsonArray
        {
            new JsonObject
            {
                ["docTrackId"] = 0,
                ["studentUid"] = studentUid,
                ["docDate"] = acceptedAt,
                ["docNameId"] = GetTrackingInt("DocumentNameId"),
                ["docDescription"] = description,
                ["docStatusId"] = GetTrackingInt("DocumentStatusId"),
                ["comments"] = "",
                ["reference"] = reference,
                ["compDate"] = acceptedAt,
                ["userDefinedFieldId"] = GetTrackingInt("UserDefinedFieldId"),
                ["finAidYearSeq"] = null,
                ["userId"] = GetTrackingSetting("UserId", "CampusFlow"),
                ["docGroupID"] = null,
                ["locationId"] = GetTrackingInt("LocationId"),
                ["admissionGpagroupId"] = null,
                ["internal"] = GetTrackingBool("Internal"),
                ["isProspectDoc"] = false
            }
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "api/student/document-tracking/add", token);
        httpRequest.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (await FindDocumentIdAsync(studentUid, reference, cancellationToken) is { } documentId)
                return documentId.ToString(CultureInfo.InvariantCulture);
            await Task.Delay(250, cancellationToken);
        }
        throw new InvalidOperationException("Elements created the Document Tracking record but its identifier could not be verified.");
    }

    public async Task<bool> HasImageAsync(string documentTrackingId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(documentTrackingId, out var documentId)) return false;
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, $"api/student/document-tracking/viewdocimage/{documentId}", token);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode) return false;
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.Length == 0) return false;
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return true;
        try
        {
            var root = JsonNode.Parse(content);
            return root?["isSuccess"]?.GetValue<bool>() == true && root["data"] is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task UploadImageAsync(string documentTrackingId, string fileName, byte[] contents,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(documentTrackingId, out var documentId))
            throw new ArgumentException("The Elements Document Tracking identifier is invalid.", nameof(documentTrackingId));
        var token = await GetAccessTokenAsync(cancellationToken);
        var trackingModule = Uri.EscapeDataString(GetTrackingSetting("TrackingModule", "Billing"));
        var displayInPortal = GetTrackingBool("DisplayInPortal").ToString().ToLowerInvariant();
        using var request = CreateRequest(HttpMethod.Post,
            $"api/student/document-tracking/image?docTrackId={documentId}&displayInPortal={displayInPortal}&module={trackingModule}", token);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(contents);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "uploadingFileObj", Path.GetFileName(fileName));
        request.Content = form;
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(content)?["isSuccess"]?.GetValue<bool>() == false)
            throw new InvalidOperationException("Elements did not accept the approved bill image.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = CreateRequest(HttpMethod.Get, "api/Login/Authenticate");
        var credentials = $"{GetApiSetting("Username")}:{GetApiSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements authentication did not return a token.");
        _memoryCache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? token = null)
    {
        var request = new HttpRequestMessage(method, $"{GetApiSetting("BaseUrl").TrimEnd('/')}/{relativeUrl.TrimStart('/')}");
        request.Headers.Add("TenantHost", GetApiSetting("TenantHost"));
        request.Headers.Add("Module", GetTrackingSetting("ApiModule", "Admissions"));
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<int?> FindDocumentIdAsync(int studentUid, string reference, CancellationToken cancellationToken)
    {
        var connectionName = _configuration[
            "StudentInformationSystems:Providers:ThesisElements:ConnectionStringName"] ?? "ThesisElementsReadOnly";
        var connectionString = _configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Thesis Elements read-only connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP 1 DocTrackID
            FROM CAMS_DocTrack_View
            WHERE StudentUID = @studentUid AND Reference = @reference
            ORDER BY DocTrackID DESC
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@studentUid", studentUid);
        command.Parameters.AddWithValue("@reference", reference);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private void EnsureEnabled()
    {
        if (!_configuration.GetValue($"{TrackingConfigurationPath}:Enabled", false))
            throw new InvalidOperationException("Elements Document Tracking delivery is not enabled.");
    }

    private string GetApiSetting(string name) => _configuration[$"{ApiConfigurationPath}:{name}"] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.");
    private string GetTrackingSetting(string name, string fallback) =>
        _configuration[$"{TrackingConfigurationPath}:{name}"] is { Length: > 0 } value ? value : fallback;
    private int GetTrackingInt(string name) => _configuration.GetValue<int>($"{TrackingConfigurationPath}:{name}");
    private bool GetTrackingBool(string name) => _configuration.GetValue<bool>($"{TrackingConfigurationPath}:{name}");
}
