using System;
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

namespace CampusFlow.CostTypes;

// Manual validation runs are restricted to the verified non-production Elements tenant.
[ExposeServices(typeof(ICostTypeElements))]
public sealed class ElementsCostTypeService : ICostTypeElements, ITransientDependency
{
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private const string TokenCacheKey = "ThesisElements:NonProductionCostTypes:IntegrationToken";
    private static readonly HttpClient SharedHttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _client;

    public ElementsCostTypeService(IConfiguration configuration, IMemoryCache cache)
        : this(configuration, cache, SharedHttpClient) { }

    internal ElementsCostTypeService(IConfiguration configuration, IMemoryCache cache, HttpClient client)
    {
        _configuration = configuration;
        _cache = cache;
        _client = client;
    }

    public void EnsureRunEnabled()
    {
        if (!_configuration.GetValue<bool>("CostTypes:EnableNonProductionRuns") ||
            !string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            !MatchesHost(Setting("BaseUrl"), "app-elm-nonprod-usc-integrationapi.azurewebsites.net") ||
            !MatchesHost(Setting("RegistrationBaseUrl"), "np-usc-gateway.thesiscloud.com") ||
            !string.Equals(Setting("TenantHost"), "el-np-nel.thesiscloud.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cost-type runs are enabled only for the verified Nelson non-production Elements tenant.");
        var connectionString = _configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The non-production Elements replica is not configured.");
        var replica = new SqlConnectionStringBuilder(connectionString);
        if (!MatchesReplicaHost(replica.DataSource) ||
            !string.Equals(replica.InitialCatalog, "EL-NP-NEL-DB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cost-type runs require the Nelson non-production Elements replica.");
    }

    public async Task<List<CostTypeChoice>> GetChoicesAsync()
    {
        await using var connection=new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync();
        await using var command=new SqlCommand("SELECT CostTypeID,CostType,Description FROM dbo.CostType WHERE ActiveFlag=1 ORDER BY CostType,CostTypeID",connection);
        await using var reader=await command.ExecuteReaderAsync(); var result=new List<CostTypeChoice>();
        while(await reader.ReadAsync()) result.Add(new(reader.GetInt32(0),reader.GetString(1).TrimEnd(),reader.IsDBNull(2)?"":reader.GetString(2).TrimEnd()));
        return result;
    }
    public async Task<List<CostTypePreviewRow>> PreviewAsync(List<CostTypeRule> rules,int? studentUid)
    {
        await using var connection=new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync();
        await using var command=new SqlCommand(PreviewSql,connection) { CommandTimeout=120 };
        command.Parameters.Add("@Rules",System.Data.SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(rules);
        command.Parameters.Add("@StudentUid",System.Data.SqlDbType.Int).Value=(object?)studentUid??DBNull.Value;
        await using var reader=await command.ExecuteReaderAsync();var rows=new List<CostTypePreviewRow>();
        string? Text(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
        while(await reader.ReadAsync()) rows.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),
            reader.GetInt32(3),reader.GetInt32(4),reader.GetString(5),Text(6),Text(7),Text(8),
            reader.GetInt32(9),reader.GetString(10),reader.IsDBNull(11)?null:reader.GetInt32(11),
            reader.IsDBNull(12)?null:reader.GetInt32(12),Text(13),reader.GetString(14)));
        return rows;
    }
    private const string PreviewSql="""
        WITH Rules AS (
          SELECT * FROM OPENJSON(@Rules) WITH (
            Priority int,Campus nvarchar(160),Attendance nvarchar(160),DegreeMatch varchar(10),
            Degree nvarchar(200),CostType nvarchar(100),CostTypeId int,Enabled bit)
        ), Records AS (
          SELECT S.StudentUID,S.StudentID,S.LastName+', '+S.FirstName Name,
            SS.StudentStatusID,SS.TermCalendarID,SS.TextTerm,SS.Campus,SP.MajorDegree,
            CASE WHEN U.UDefLookup1Value IS NULL OR U.UDefLookup1Value='' THEN S.AttendanceType ELSE U.UDefLookup1Value END AttendanceType,
            SS.CostTypeID,SS.CostType,COUNT(*) OVER(PARTITION BY S.StudentUID,SS.TermCalendarID) JoinedRows
          FROM dbo.CAMS_Student_View S JOIN dbo.CAMS_StudentStatus_View SS ON SS.StudentUID=S.StudentUID
          LEFT JOIN dbo.CAMS_StudentStatusUserDefined_View U ON U.StudentStatusID=SS.StudentStatusID
          LEFT JOIN dbo.CAMS_StudentProgram_View SP ON SP.StudentStatusID=SS.StudentStatusID AND SP.PriProgID=1
          WHERE SS.Term >= (SELECT TOP 1 Term FROM dbo.TermCalendar WHERE TermEndDate>=GETDATE()
            AND TextTerm NOT IN ('Spring 2024','Fall 2024','Fall 2025','Spring 2026') ORDER BY Term)
            AND (@StudentUid IS NULL OR S.StudentUID=@StudentUid)
        )
        SELECT R.StudentUID,R.StudentID,R.Name,R.StudentStatusID,R.TermCalendarID,R.TextTerm,
          R.Campus,R.AttendanceType,R.MajorDegree,R.CostTypeID,R.CostType,M.Priority,C.TargetId,M.CostType,
          CASE WHEN R.JoinedRows<>1 THEN 'Duplicate record'
               WHEN M.Priority IS NULL THEN 'Unmapped'
               WHEN C.Matches<>1 THEN 'Invalid lookup'
               WHEN R.CostTypeID=C.TargetId THEN 'Already correct' ELSE 'Would change' END Action
        FROM Records R
        OUTER APPLY(SELECT TOP 1 * FROM Rules X WHERE X.Enabled=1
          AND (X.Campus IS NULL OR LTRIM(RTRIM(R.Campus))=X.Campus)
          AND LTRIM(RTRIM(R.AttendanceType))=X.Attendance
          AND (X.DegreeMatch='ANY' OR (X.DegreeMatch='NULL' AND R.MajorDegree IS NULL)
            OR (X.DegreeMatch='EXACT' AND LTRIM(R.MajorDegree)=X.Degree)) ORDER BY X.Priority) M
        OUTER APPLY(SELECT COUNT(*) Matches,MIN(CostTypeID) TargetId FROM dbo.CostType
          WHERE ActiveFlag=1 AND CostType=M.CostType AND CostTypeID=M.CostTypeId) C
        ORDER BY R.TextTerm,R.Name,R.StudentUID;
        """;
    private async Task<JsonObject> GetStatusAsync(CostTypePreviewRow row)
    {
        EnsureRunEnabled();var token=await GetTokenAsync(default);
        using var request=Request(HttpMethod.Get,$"/api/academic/register/student-status/{row.TermId}/{row.StudentUid}",token);
        using var response=await _client.SendAsync(request);
        var root=await ReadAsync(response,default);
        var data=root["data"]?.AsObject() ?? throw new InvalidOperationException("Missing student status.");
        if(ReadInt(data,"studentStatusID")!=row.StatusId || ReadInt(data,"termCalendarID")!=row.TermId)
            throw new InvalidOperationException("Elements returned a different student-term status.");
        return BuildPayload(data);
    }
    public async Task<string> FingerprintAsync(CostTypePreviewRow row)
    {
        var status=await GetStatusAsync(row);
        if(ReadInt(status,"costTypeID")!=row.CurrentId) throw new InvalidOperationException("Elements changed since the reporting replica. Preview again after it refreshes.");
        return Fingerprint(status);
    }
    public async Task ApplyAsync(CostTypeCandidate candidate)
    {
        EnsureRunEnabled();var row=candidate.Row;
        if(row.Action!="Would change" || row.ProposedId is null || row.ProposedId<0)
            throw new InvalidOperationException("No validated cost-type change.");
        var choices=await GetChoicesAsync();
        if(choices.Count(x=>x.Id==row.ProposedId && x.Label==row.ProposedLabel?.TrimEnd())!=1)
            throw new InvalidOperationException("Cost type changed since preview.");
        var status=await GetStatusAsync(row);
        status=PrepareChange(candidate,status);
        var token=await GetTokenAsync(default);
        using var request=Request(HttpMethod.Put,"/api/academic/register/student-status",token);
        request.Content=new StringContent(status.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await _client.SendAsync(request);await ReadAsync(response,default);
        var after=await GetStatusAsync(row);
        if(!JsonNode.DeepEquals(status,after)) throw new InvalidOperationException("Update returned a different status. Verify in Elements.");
    }
    public static JsonObject PrepareChange(CostTypeCandidate candidate,JsonObject status)
    {
        var row=candidate.Row;
        if(row.Action!="Would change" || row.ProposedId is null || row.ProposedId<0 ||
            ReadInt(status,"studentStatusID")!=row.StatusId || ReadInt(status,"termCalendarID")!=row.TermId ||
            ReadInt(status,"costTypeID")!=row.CurrentId || Fingerprint(status)!=candidate.ApiFingerprint)
            throw new InvalidOperationException("Student status changed since preview.");
        var result=(JsonObject)status.DeepClone();result["costTypeID"]=row.ProposedId.Value;return result;
    }
    public static string Fingerprint(JsonObject value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString())));
    public static JsonObject BuildPayload(JsonObject source)
    {
        string[] names=["studentStatusID","termCalendarID","statEffectiveDate","enrollmentStatusID",
            "academicStatusID","registrationStatusID","campusID","alert","collegeLevelID","studentLevel",
            "costTypeID","refundTypeID","ftptStatusID","maxAllowedHours","financialAid","residentCommuterID",
            "gpaGroupingID","cohortGroupID","classificationID","vocationID","chargeInsurance","gradecatalogID",
            "nonStateReporting","degreeSeeking","lastDateOfAttendance","institutionalSAPID","studentWorker",
            "governmentalSAPID","commentPosition","transcriptComment","leaveOfAbsence","leaveOfAbsenceDue"];
        var result=new JsonObject();
        foreach(var name in names)
        {
            if(!source.ContainsKey(name)) throw new InvalidOperationException("Elements status response is missing an expected field.");
            result[name]=source[name]?.DeepClone();
        }
        return result;
    }
    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        EnsureRunEnabled();
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Non-production Elements authentication failed.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonNode.Parse(body)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Non-production Elements did not return an authentication token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage Request(HttpMethod method, string route, string token)
    {
        var tenantHost = Setting("TenantHost");
        var request = new HttpRequestMessage(method, $"{Setting("BaseUrl").TrimEnd('/')}{route}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "Registration");
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
        if (!response.IsSuccessStatusCode || root?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"Non-production Elements rejected the cost-type request (HTTP {(int)response.StatusCode}).");
        return root;
    }

    private string Setting(string name) => _configuration[ApiPath + name]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private static bool MatchesHost(string value, string host) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesReplicaHost(string value) =>
        string.Equals(value, "sql-elm-nonprod-usc-replica.database.windows.net", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433", StringComparison.OrdinalIgnoreCase);

    private static int ReadInt(JsonObject value, string key) => value[key]?.GetValue<int>() ?? 0;
    private static string ReadString(JsonObject value, string key) => value[key]?.GetValue<string>()?.Trim() ?? "";
}
