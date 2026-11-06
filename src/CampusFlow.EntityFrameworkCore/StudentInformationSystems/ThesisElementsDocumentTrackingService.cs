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
    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public ThesisElementsDocumentTrackingService(IConfiguration configuration, IMemoryCache memoryCache)
    : this(configuration, memoryCache, SharedClient) { }

    internal ThesisElementsDocumentTrackingService(IConfiguration configuration, IMemoryCache memoryCache, HttpClient client)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
        _client = client;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<string> CreateApprovedBillAsync(
        StudentDocumentTrackingRequest request, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (!int.TryParse(request.ExternalStudentId, out var studentUid) || studentUid <= 0 || request.ApprovalId == Guid.Empty)
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(request));

        var reference = request.ApprovalId.ToString("N");
        if (await FindDocumentIdAsync(studentUid, reference, cancellationToken) is { } existingId)
            return existingId.ToString(CultureInfo.InvariantCulture);

        if (!request.AllowCreate)
            throw new CampusFlow.BillApprovals.BillCompletionPreparationException("A document creation was already attempted. Retry can recover an existing record by approval reference; an administrator must confirm no record exists before allowing another creation.");
        var token = await GetAccessTokenAsync(cancellationToken);
        // Elements requires the document type's configured description, including spacing.
        using var typesRequest = CreateRequest(HttpMethod.Get, "api/manager/lookup-table-options/doc-track-setup/documents", token);
        using var typesResponse = await _client.SendAsync(typesRequest, cancellationToken);
        typesResponse.EnsureSuccessStatusCode();
        var typesBody = JsonNode.Parse(await typesResponse.Content.ReadAsStringAsync(cancellationToken));
        if (typesBody?["isSuccess"]?.GetValue<bool>() != true || typesBody["data"] is not JsonArray types)
            throw new InvalidOperationException("Elements document types could not be verified.");
        var matches = types.Where(x => x?["uniqueID"]?.GetValue<int>() == GetTrackingInt("DocumentNameId")).ToArray();
        if (matches.Length != 1 || matches[0]?["activeFlag"]?.GetValue<bool>() != true ||
            matches[0]?["defaultLocationID"]?.GetValue<int>() != GetTrackingInt("LocationId") ||
            string.IsNullOrWhiteSpace(matches[0]?["description"]?.GetValue<string>()))
            throw new InvalidOperationException("The approved bill document type or location is missing, inactive or mismatched.");
        var description = matches[0]!["description"]!.GetValue<string>();
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
                ["comments"] = "CampusFlow approved bill - " + request.TermName,
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
        using var response = await _client.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(content)?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not confirm the document creation request.");
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (await FindDocumentIdAsync(studentUid, reference, cancellationToken) is { } documentId)
                return documentId.ToString(CultureInfo.InvariantCulture);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new InvalidOperationException("The document creation result could not be verified. Review Elements before allowing another creation.");
    }

    public async Task<bool> HasImageAsync(string documentTrackingId, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (!int.TryParse(documentTrackingId, out var documentId) || documentId <= 0) throw new ArgumentException("A valid document tracking identifier is required.");
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, $"api/student/document-tracking/viewdocimage/{documentId}", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.Length == 0) return false;
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return true;
        var root = JsonNode.Parse(content);
        if (root?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements could not confirm whether the approved bill image exists.");
        return root["data"] is not null;
    }

    public async Task UploadImageAsync(string documentTrackingId, string fileName, byte[] contents,
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (!int.TryParse(documentTrackingId, out var documentId) || documentId <= 0)
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
        using var response = await _client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(content)?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not accept the approved bill image.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = CreateRequest(HttpMethod.Get, "api/Login/Authenticate");
        var credentials = $"{GetApiSetting("Username")}:{GetApiSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await _client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements authentication did not return a token.");
        _memoryCache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? token = null)
    {
        var baseUrl = relativeUrl.StartsWith("api/Login/", StringComparison.OrdinalIgnoreCase) || relativeUrl.StartsWith("api/manager/", StringComparison.OrdinalIgnoreCase) || relativeUrl == "api/student/document-tracking/add" ? GetApiSetting("BaseUrl") : DocumentBaseUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}");
        if (!relativeUrl.StartsWith("api/Login/", StringComparison.OrdinalIgnoreCase))
        {
            var origin = "https://" + GetApiSetting("TenantHost").TrimEnd('/');
            request.Headers.Add("Origin", origin); request.Headers.Referrer = new Uri(origin);
        }
        request.Headers.Add("TenantHost", GetApiSetting("TenantHost"));
        request.Headers.Add("Module", relativeUrl.StartsWith("api/manager/", StringComparison.OrdinalIgnoreCase) ? "Manager" : GetTrackingSetting("ApiModule", "Admissions"));
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
        if (IsNonProduction)
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            if (!(string.Equals(parsed.DataSource, "sql-elm-nonprod-usc-replica.database.windows.net", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(parsed.DataSource, "tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433", StringComparison.OrdinalIgnoreCase)) ||
                !string.Equals(parsed.InitialCatalog, "EL-NP-NEL-DB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nonprod document tracking must use the nonprod replica.");
        }
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

    private string DocumentBaseUrl => GetTrackingSetting("ApiBaseUrl", _configuration[$"{ApiConfigurationPath}:RegistrationBaseUrl"] ?? GetApiSetting("BaseUrl"));
    private bool IsNonProduction => string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"], "Staging", StringComparison.OrdinalIgnoreCase);
    private void EnsureEnabled()
    {
        var root = new Uri(DocumentBaseUrl.TrimEnd('/') + "/");
        var auth = new Uri(GetApiSetting("BaseUrl").TrimEnd('/') + "/");
        if (auth.Scheme != "https" || !auth.IsDefaultPort || auth.AbsolutePath != "/" || auth.Query.Length != 0 || auth.Fragment.Length != 0 || auth.UserInfo.Length != 0 ||
            root.Scheme != "https" || !root.IsDefaultPort || root.AbsolutePath != "/" || root.Query.Length != 0 || root.Fragment.Length != 0 || root.UserInfo.Length != 0 ||
            IsNonProduction && (!string.Equals(root.Host, "np-usc-gateway.thesiscloud.com", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(auth.Host, "app-elm-nonprod-usc-integrationapi.azurewebsites.net", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetApiSetting("TenantHost"), "el-np-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Document tracking must target the correct Elements environment.");
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
