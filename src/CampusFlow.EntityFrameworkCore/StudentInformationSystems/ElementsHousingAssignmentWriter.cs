using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
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
using CampusFlow.Housing;

namespace CampusFlow.StudentInformationSystems;

// Room-assignment writes require an explicit flag and the exact matching Elements environment.
public sealed partial class ElementsHousingAssignmentWriter : IElementsHousingAssignmentWriter, ITransientDependency
{
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private const string TokenCacheKey = "ThesisElements:Housing:IntegrationToken";
    private static readonly HttpClient SharedHttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _client;
    private readonly Func<int,int,int,int,string,CancellationToken,Task<bool>> _deletedAssignment;

    public ElementsHousingAssignmentWriter(IConfiguration configuration, IMemoryCache cache)
        : this(configuration, cache, SharedHttpClient) { }

    internal ElementsHousingAssignmentWriter(IConfiguration configuration, IMemoryCache cache, HttpClient client,
        Func<int,int,int,int,string,CancellationToken,Task<bool>>? deletedAssignment = null)
    {
        _configuration = configuration;
        _cache = cache;
        _client = client;
        _deletedAssignment = deletedAssignment ?? IsDeletedAssignmentAsync;
    }

    public void EnsureWriteEnabled()
    {
        var nonProduction = string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase);
        var production = string.IsNullOrWhiteSpace(_configuration["App:EnvironmentLabel"]);
        if (!((nonProduction && _configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
                !_configuration.GetValue<bool>("Housing:EnableProductionWrites") &&
                MatchesHost(Setting("BaseUrl"), "app-elm-nonprod-usc-integrationapi.azurewebsites.net") &&
                MatchesHost(Setting("RegistrationBaseUrl"), "np-usc-gateway.thesiscloud.com") &&
                string.Equals(Setting("TenantHost"), "el-np-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase)) ||
              (production && _configuration.GetValue<bool>("Housing:EnableProductionWrites") &&
                !_configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
                MatchesHost(Setting("BaseUrl"), "app-elm-prod-usc-integrationapi.azurewebsites.net") &&
                MatchesHost(Setting("RegistrationBaseUrl"), "el-usc-gateway.thesiscloud.com") &&
                string.Equals(Setting("TenantHost"), "el-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Housing writes require an explicitly enabled and verified Nelson Elements environment.");
        var connectionString = _configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Elements read-only replica is not configured.");
        var replica = new SqlConnectionStringBuilder(connectionString);
        if (!MatchesReplicaHost(replica.DataSource, nonProduction
                ? "sql-elm-nonprod-usc-replica.database.windows.net"
                : "sql-elm-prod-usc-replica.database.windows.net") ||
            !string.Equals(replica.InitialCatalog, nonProduction ? "EL-NP-NEL-DB" : "EL-NEL-DB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Housing writes require the matching Nelson Elements replica.");
    }

    public async Task<(string Building, string Room)?> GetAssignedRoomAsync(int studentUid, int periodId,
        CancellationToken cancellationToken = default)
    {
        EnsureWriteEnabled();
        var token = await GetTokenAsync(cancellationToken);
        using var request = Request(HttpMethod.Get,
            $"/api/student-life/housing/assign-room/get-assigned-rooms/{studentUid}", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        var data = root["data"] as JsonArray;
        if (data is null) throw new InvalidOperationException("Elements returned an invalid assignment list.");
        var matches = new List<(string Building, string Room)>();
        if (data.Any(x => x is not JsonObject))
            throw new InvalidOperationException("Elements returned an invalid assignment row.");
        foreach (var row in data.OfType<JsonObject>().Where(x => ReadInt(x, "housingPeriodID") == periodId))
        {
            var propertyId = ReadInt(row, "housingPeriodPropertyID");
            var building = ReadString(row, "building").Trim();
            var roomName = ReadString(row, "roomName").Trim();
            if (propertyId <= 0 || building.Length == 0 || roomName.Length == 0)
                throw new InvalidOperationException("Elements returned an incomplete room identity.");
            var property = (await HousingDataAsync(
                $"/api/student-life/housing/housing-periods/housing-period-properties/{propertyId}", cancellationToken)).AsObject();
            if (ReadInt(property, "housingPeriodPropertyID") != propertyId ||
                ReadInt(property, "housingPeriodID") != periodId ||
                !string.Equals(ReadString(property, "propertyName").Trim(), building, StringComparison.OrdinalIgnoreCase) ||
                property["rooms"] is not JsonArray rooms || rooms.Any(x => x is not JsonObject))
                throw new InvalidOperationException("The live housing property identity could not be verified.");
            // The student endpoint retains deleted assignments after a period's rooms are replaced.
            // Only a room still returned by the live property's current catalog can be assigned.
            var current = rooms.OfType<JsonObject>()
                .Where(x => x["isDeleted"]?.GetValue<bool>() != true &&
                    string.Equals(ReadString(x, "roomName").Trim(), roomName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (current.Count == 0)
            {
                var roomId = ReadInt(row, "roomID");
                if (roomId <= 0) throw new InvalidOperationException("Elements omitted the assigned room identifier.");
                // Absence from the property alone is not proof of deallocation. Keep the
                // original live conflict unless the exact matching assignment is tombstoned.
                if (await _deletedAssignment(studentUid, periodId, propertyId, roomId, roomName, cancellationToken)) continue;
                matches.Add((building, roomName));
                continue;
            }
            if (current.Count != 1 || ReadInt(current[0], "housingPeriodRoomID") <= 0 ||
                ReadInt(current[0], "housingPeriodID") != periodId)
                throw new InvalidOperationException("The live room identity is ambiguous or belongs to another period.");
            matches.Add((building, roomName));
        }
        if (matches.Count > 1)
            throw new InvalidOperationException("Elements returned multiple room assignments for one student and period.");
        if (matches.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(matches[0].Building) || string.IsNullOrWhiteSpace(matches[0].Room))
            throw new InvalidOperationException("Elements returned an assignment without a building or room.");
        return matches[0];
    }

    private async Task<bool> IsDeletedAssignmentAsync(int studentUid, int periodId, int propertyId,
        int roomId, string roomName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN a.IsDeleted=0 THEN 1 ELSE 0 END),0)
            FROM StudentLife.HousingPeriodRoomAssignments a
            JOIN StudentLife.HousingPeriodRooms r ON r.HousingPeriodRoomID=a.HousingPeriodRoomID
                AND r.HousingPeriodID=a.HousingPeriodID
            WHERE a.StudentUID=@Student AND a.HousingPeriodID=@Period
                AND r.HousingPeriodPropertyID=@Property
                AND (r.HousingPeriodRoomID=@Room OR r.OriginalRoomID=@Room)
                AND LTRIM(RTRIM(r.RoomName))=@Name;
            """, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@Student", SqlDbType.Int).Value=studentUid;
        command.Parameters.Add("@Period", SqlDbType.Int).Value=periodId;
        command.Parameters.Add("@Property", SqlDbType.Int).Value=propertyId;
        command.Parameters.Add("@Room", SqlDbType.Int).Value=roomId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value=roomName;
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("The assignment history could not be verified.");
        return reader.GetInt32(0)>0 && reader.GetInt32(1)==0;
    }

    public Task AssignAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync("assign", studentUid, periodId, periodRoomId, cancellationToken);

    public Task DeallocateAsync(int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync("deallocate", studentUid, periodId, periodRoomId, cancellationToken);

    private async Task ChangeAsync(string action, int studentUid, int periodId, int periodRoomId,
        CancellationToken cancellationToken)
    {
        EnsureWriteEnabled();
        if (studentUid <= 0 || periodId <= 0 || periodRoomId <= 0)
            throw new ArgumentOutOfRangeException(nameof(studentUid), "A valid student, period, and room are required.");
        var token = await GetTokenAsync(cancellationToken);
        using var request = Request(HttpMethod.Post,
            $"/api/student-life/housing/housing-periods/room-assignments/{action}", token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            studentUIds = new[] { studentUid }, housingPeriodRoomId = periodRoomId,
            housingPeriodId = periodId, housingApplicationFormId = 0
        }), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        // Some vendor responses add a second business outcome inside the envelope.
        // Honor an explicit failure; older/minimal envelopes still require the caller's
        // existing live assignment readback before an operation can be marked verified.
        if (root["data"] is JsonObject outcome && outcome.ContainsKey("success") &&
            (outcome["success"] is not JsonValue success || !success.TryGetValue<bool>(out var applied) || !applied))
            throw new HousingRequestException(HousingApplyFailure.ResponseCode((int)response.StatusCode,
                root["data"]?.ToJsonString() ?? ""), (int)response.StatusCode);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        EnsureWriteEnabled();
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Elements housing authentication failed.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonNode.Parse(body)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Elements did not return a housing authentication token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage Request(HttpMethod method, string route, string token)
    {
        var tenantHost = Setting("TenantHost");
        var request = new HttpRequestMessage(method, $"{Setting("BaseUrl").TrimEnd('/')}{route}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "StudentLife");
        request.Headers.Add("TenantHost", tenantHost);
        request.Headers.Add("Origin", $"https://{tenantHost}");
        request.Headers.Referrer = new Uri($"https://{tenantHost}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? root;
        try { root = JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { root = null; }
        if (!response.IsSuccessStatusCode || root?["isSuccess"] is not JsonValue success ||
            !success.TryGetValue<bool>(out var accepted) || !accepted)
        {
            var code = HousingApplyFailure.ResponseCode((int)response.StatusCode, body);
            throw new HousingRequestException(code, (int)response.StatusCode);
        }
        return root;
    }

    private string Setting(string name) => _configuration[ApiPath + name]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private static bool MatchesHost(string value, string host) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesReplicaHost(string value, string host) =>
        string.Equals(value, host, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, $"tcp:{host},1433", StringComparison.OrdinalIgnoreCase);

    private static int ReadInt(JsonObject value, string key) => value[key]?.GetValue<int>() ?? 0;
    private static string ReadString(JsonObject value, string key) => value[key]?.GetValue<string>()?.Trim() ?? "";
}
