using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

public sealed class ThesisElementsAwardCodeMaintenance : IStudentInformationSystemAwardCodeMaintenance
{
    private const string ConnectionName = "ThesisElementsReadOnly";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient _client;
    private readonly Func<string,int,CancellationToken,Task<IReadOnlyList<AwardCodeMismatch>>> _preview;
    private const string ApiPath="StudentInformationSystems:Providers:ThesisElements:Api:";

    public ThesisElementsAwardCodeMaintenance(IConfiguration configuration, IMemoryCache cache)
        :this(configuration,cache,SharedClient) { }
    internal ThesisElementsAwardCodeMaintenance(IConfiguration configuration, IMemoryCache cache,HttpClient client,
        Func<string,int,CancellationToken,Task<IReadOnlyList<AwardCodeMismatch>>>? preview=null)
    {_configuration=configuration;_cache=cache;_client=client;_preview=preview??GetMismatchesAsync;}

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
    public bool CanWrite
    {
        get
        {
            try { EnsureWriteEnabled();return true; }
            catch(InvalidOperationException){return false;}
        }
    }
    private void EnsureWriteEnabled()
    {
        _=GetConnectionString();
        var np=string.Equals(_configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase);
        if(!_configuration.GetValue<bool>("AwardCodeMaintenance:Enabled") ||
            !_configuration.GetValue<bool>(np?"AwardCodeMaintenance:EnableNonProductionWrites":"AwardCodeMaintenance:EnableProductionWrites") ||
            _configuration.GetValue<bool>(np?"AwardCodeMaintenance:EnableProductionWrites":"AwardCodeMaintenance:EnableNonProductionWrites") ||
            !Host(_configuration[ApiPath+"BaseUrl"],np?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"app-elm-prod-usc-integrationapi.azurewebsites.net") ||
            _configuration[ApiPath+"TenantHost"]!=(np?"el-np-nel.thesiscloud.com":"el-nel.thesiscloud.com"))
            throw new InvalidOperationException("Award-code writes require explicitly enabled matching Elements environments.");
    }
    private static bool Host(string? value,string host)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host==host&&uri.IsDefaultPort&&uri.AbsolutePath=="/"&&uri.Query==""&&uri.Fragment==""&&uri.UserInfo=="";

    public async Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP (50) LTRIM(RTRIM(TextTerm)) FROM dbo.TermCalendar " +
                           "WHERE TextTerm IS NOT NULL ORDER BY TermCalendarID DESC";
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        var terms = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var term = reader.GetString(0);
            if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase)) terms.Add(term);
        }
        return terms;
    }

    public async Task<IReadOnlyList<AwardCodeMismatch>> GetMismatchesAsync(string term, int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || take is < 1 or > 101)
            throw new ArgumentOutOfRangeException(nameof(take));
        const string sql = """
            SELECT TOP (@Take) f.FinancialAwardID, f.StudentUID, s.StudentID,
                   LTRIM(RTRIM(s.FirstName)) + ' ' + LTRIM(RTRIM(s.LastName)),
                   tc.TextTerm, f.[Description], f.Code1ID, fr.Code1ID,
                   f.ShowOnBillingStatement, fr.ShowOnBillStatement, f.TermCalendarID
            FROM dbo.FinancialAward f
            JOIN dbo.FinancialAwardRef fr ON fr.AwardTypeID = f.TransTypeID
            JOIN dbo.TermCalendar tc ON tc.TermCalendarID = f.TermCalendarID
            JOIN dbo.Student s ON s.StudentUID = f.StudentUID
            WHERE tc.TextTerm = @Term
              AND (ISNULL(f.Code1ID, 0) <> ISNULL(fr.Code1ID, 0)
                   OR COALESCE(f.ShowOnBillingStatement, '') <> COALESCE(fr.ShowOnBillStatement, ''))
            ORDER BY f.FinancialAwardID
            """;
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        command.Parameters.Add("@Term", SqlDbType.VarChar, 50).Value = term.Trim();
        var rows = new List<AwardCodeMismatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new AwardCodeMismatch(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2).Trim(),
                reader.GetString(3).Trim(), reader.GetString(4).Trim(),
                reader.IsDBNull(5) ? "Award" : reader.GetString(5).Trim(),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                !reader.IsDBNull(8) && IsYes(reader.GetValue(8)),
                !reader.IsDBNull(9) && IsYes(reader.GetValue(9)),reader.GetInt32(10)));
        return rows;
    }

    public async Task UpdateAsync(AwardCodeMismatch expected,CancellationToken cancellationToken=default)
    {
        EnsureWriteEnabled();
        if(expected.AwardId<=0||expected.StudentUid<=0||expected.TermCalendarId<=0)throw new InvalidOperationException("Preview one valid award and term before applying changes.");
        var rows=await _preview(expected.Term,101,cancellationToken);
        if(rows.SingleOrDefault(x=>x.AwardId==expected.AwardId)!=expected)
            throw new InvalidOperationException("The award or its reference values changed. Preview again before applying.");
        var token=await Token(cancellationToken);var award=await ReadAward(expected,token,cancellationToken);
        var body=BuildReconciliationBody(award,expected);
        if(AlreadyCorrect(award,expected))return; // An earlier unknown PUT may already have succeeded.
        // Check again immediately before the full-field PUT. The vendor endpoint has no ETag contract.
        var fresh=await ReadAward(expected,token,cancellationToken);
        if(!JsonNode.DeepEquals(award,fresh))throw new InvalidOperationException("The live award changed during review. No update was sent.");
        using var request=Request(HttpMethod.Put,$"/api/financial-aid/maintenance/awards/financial-awards/{expected.AwardId}",token);
        request.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await _client.SendAsync(request,cancellationToken);var result=await response.Content.ReadAsStringAsync(cancellationToken);
        if(!response.IsSuccessStatusCode||JsonNode.Parse(result)?["isSuccess"]?.GetValue<bool>()!=true)
            throw new InvalidOperationException("The award update result is unconfirmed. Check Elements before retrying.");
        var after=await ReadAward(expected,token,cancellationToken);VerifyBody(body,after);
    }
    internal static JsonObject BuildReconciliationBody(JsonObject award,AwardCodeMismatch expected)
    {
        if(award["financialAwardId"]?.GetValue<int>()!=expected.AwardId || award["studentUid"]?.GetValue<int>()!=expected.StudentUid ||
            award["termCalendarId"]?.GetValue<int>()!=expected.TermCalendarId)
            throw new InvalidOperationException("The live award does not match the reviewed student and term.");
        if(award["lock"]?.GetValue<bool>()==true)throw new InvalidOperationException("The award is locked and requires Financial Aid review.");
        var body=ThesisElementsFinancialAidDecisionService.BuildPreservingBody(award);
        if(!AlreadyCorrect(award,expected)&&((NullableInt(award["code1Id"])??0)!=(expected.CurrentCodeId??0) || YesNo(award["showOnBillingStatement"])!=expected.CurrentShowOnBill))
            throw new InvalidOperationException("The current award code or billing flag changed since preview.");
        body["code1Id"]=expected.ReferenceCodeId??0;
        body["studentAccepted"]=Decision(award["studentAccepted"]);
        body["showOnBillingStatement"]=expected.ReferenceShowOnBill?"Yes":"No";
        return body;
    }
    private static int? Decision(JsonNode? value)
    {
        if(value is null)return null;
        if(value is JsonValue j && j.TryGetValue<bool>(out var b))return b?1:0;
        if(value is JsonValue v && v.TryGetValue<int>(out var n) && n is 0 or 1)return n;
        throw new InvalidOperationException("Elements returned an unrecognized award acceptance value.");
    }
    private static int? NullableInt(JsonNode? value)=>value is null?null:value.GetValue<int>();
    private static bool YesNo(JsonNode? value)=>value is not null && IsYes(value.ToString());
    private static bool AlreadyCorrect(JsonObject award,AwardCodeMismatch expected)=>(NullableInt(award["code1Id"])??0)==(expected.ReferenceCodeId??0) && YesNo(award["showOnBillingStatement"])==expected.ReferenceShowOnBill;
    private static void VerifyBody(JsonObject body,JsonObject after)
    {
        foreach(var property in body)
        {
            var readName=property.Key=="transDate"?"distributionDate":property.Key;
            var equal=property.Key switch { "showOnBillingStatement"=>YesNo(property.Value)==YesNo(after[readName]), "studentAccepted"=>Decision(property.Value)==Decision(after[readName]), _=>JsonNode.DeepEquals(property.Value,after[readName]) };
            if(!after.ContainsKey(readName)||!equal)throw new InvalidOperationException($"Elements did not preserve award field {readName}. Stop and review this award before further updates.");
        }
    }
    private async Task<JsonObject> ReadAward(AwardCodeMismatch expected,string token,CancellationToken ct)
    {
        using var request=Request(HttpMethod.Get,$"/api/financial-aid/maintenance/awards/financial-awards/{expected.StudentUid}",token);
        using var response=await _client.SendAsync(request,ct);var root=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if(!response.IsSuccessStatusCode||root?["isSuccess"]?.GetValue<bool>()!=true||root["data"]?["getFinancialAwardDetails"] is not JsonArray rows || rows.Any(x=>x is not JsonObject))
            throw new InvalidOperationException("Elements did not return a valid award list.");
        var matches=rows.OfType<JsonObject>().Where(x=>x["financialAwardId"]?.GetValue<int>()==expected.AwardId&&x["studentUid"]?.GetValue<int>()==expected.StudentUid&&x["termCalendarId"]?.GetValue<int>()==expected.TermCalendarId).ToList();
        if(matches.Count!=1)throw new InvalidOperationException("The selected award is missing, ambiguous or belongs to another term.");return matches[0];
    }
    private async Task<string> Token(CancellationToken ct)
    {
        EnsureWriteEnabled();var key="AwardCodeMaintenance:"+Setting("TenantHost");
        if(_cache.TryGetValue(key,out string? cached)&&!string.IsNullOrWhiteSpace(cached))return cached;
        using var request=new HttpRequestMessage(HttpMethod.Get,Setting("BaseUrl").TrimEnd('/')+"/api/Login/Authenticate");
        request.Headers.Add("TenantHost",Setting("TenantHost"));request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username")+":"+Setting("Password"))));
        using var response=await _client.SendAsync(request,ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Award maintenance authentication failed.");
        var token=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["data"]?["token"]?.GetValue<string>();
        if(string.IsNullOrWhiteSpace(token))throw new InvalidOperationException("Elements returned no award-maintenance token.");_cache.Set(key,token,TimeSpan.FromMinutes(10));return token;
    }
    private string Setting(string name)=>_configuration[ApiPath+name]??throw new InvalidOperationException("An award-maintenance API setting is missing.");
    private HttpRequestMessage Request(HttpMethod method,string path,string token)
    {
        EnsureWriteEnabled();var request=new HttpRequestMessage(method,Setting("BaseUrl").TrimEnd('/')+path);
        request.Headers.Add("TenantHost",Setting("TenantHost"));request.Headers.Add("Module","FinancialAid");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return request;
    }

    internal static bool IsYes(object value) =>
        string.Equals(Convert.ToString(value)?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Convert.ToString(value)?.Trim(), "True", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Convert.ToString(value)?.Trim(), "1", StringComparison.OrdinalIgnoreCase);

    private string GetConnectionString()
    {
        var nonprod=string.Equals(_configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase);
        var production=string.IsNullOrWhiteSpace(_configuration["App:EnvironmentLabel"]) || string.Equals(_configuration["App:EnvironmentLabel"],"PRODUCTION",StringComparison.OrdinalIgnoreCase);
        if(!nonprod && (!production || _configuration["ASPNETCORE_ENVIRONMENT"]!="Production"))
            throw new InvalidOperationException("The Elements environment could not be verified.");
        var builder=new SqlConnectionStringBuilder(_configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException("The Elements read-only connection is missing."));
        var expectedHost=nonprod?"sql-elm-nonprod-usc-replica.database.windows.net":"sql-elm-prod-usc-replica.database.windows.net";
        if (!(string.Equals(builder.DataSource,expectedHost,StringComparison.OrdinalIgnoreCase) ||
              string.Equals(builder.DataSource,"tcp:"+expectedHost+",1433",StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(builder.InitialCatalog,nonprod?"EL-NP-NEL-DB":"EL-NEL-DB",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Award preview requires the matching Elements replica.");
        builder.ApplicationIntent=ApplicationIntent.ReadOnly;
        return builder.ConnectionString;
    }
}
