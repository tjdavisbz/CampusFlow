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
using CampusFlow.StudentInformationSystems;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
namespace CampusFlow.ClearanceResets;

[ExposeServices(typeof(IClearanceResetElements))]
public sealed class ElementsClearanceResetService:IClearanceResetElements,ITransientDependency
{
    private const string Prefix="StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly HttpClient SharedClient=new(){Timeout=TimeSpan.FromSeconds(90)};
    private readonly IConfiguration _configuration;private readonly HttpClient _http;
    private readonly Func<Mapping,CancellationToken,Task<List<ClearanceResetCandidate>>> _find;
    private readonly Func<ClearanceResetCandidate,Mapping,CancellationToken,Task<bool>> _eligible;
    private string? _token;
    internal sealed record Mapping(string Property,int LookupNumber,int Cleared,int NotCleared);
    public ElementsClearanceResetService(IConfiguration configuration)
    {_configuration=configuration;_http=SharedClient;_find=FindAsync;_eligible=EligibleAsync;}
    internal ElementsClearanceResetService(IConfiguration configuration,HttpClient http,
        Func<Mapping,CancellationToken,Task<List<ClearanceResetCandidate>>> find,
        Func<ClearanceResetCandidate,Mapping,CancellationToken,Task<bool>> eligible)
    {_configuration=configuration;_http=http;_find=find;_eligible=eligible;}
    private string Setting(string name)=>_configuration[Prefix+name]??throw new InvalidOperationException("Missing Elements configuration.");
    public void EnsureRunEnabled()
    {
        if(!_configuration.GetValue<bool>("BillApproval:ClearedStatus:Enabled"))throw new InvalidOperationException("Cleared-status updates are disabled for this environment.");
        EnsureEnvironment();
    }
    private void EnsureEnvironment()
    {
        var label=_configuration["App:EnvironmentLabel"];var np=string.Equals(label,"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase);
        var prod=(string.IsNullOrWhiteSpace(label)||string.Equals(label,"PRODUCTION",StringComparison.OrdinalIgnoreCase))&&string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"],"Production",StringComparison.OrdinalIgnoreCase);
        if(!np&&!prod)throw new InvalidOperationException("Unrecognized Elements environment.");
        CheckUrl(Setting("BaseUrl"),np?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"app-elm-prod-usc-integrationapi.azurewebsites.net");
        CheckUrl(Setting("RegistrationBaseUrl"),np?"np-usc-gateway.thesiscloud.com":"el-usc-gateway.thesiscloud.com");
        if(!string.Equals(Setting("TenantHost"),np?"el-np-nel.thesiscloud.com":"el-nel.thesiscloud.com",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Elements tenant mismatch.");
        var cs=new SqlConnectionStringBuilder(_configuration.GetConnectionString("ThesisElementsReadOnly")??throw new InvalidOperationException("Reporting connection missing."));
        var host=np?"sql-elm-nonprod-usc-replica.database.windows.net":"sql-elm-prod-usc-replica.database.windows.net";
        if(!(string.Equals(cs.DataSource,host,StringComparison.OrdinalIgnoreCase)||string.Equals(cs.DataSource,"tcp:"+host+",1433",StringComparison.OrdinalIgnoreCase))||cs.InitialCatalog!=(np?"EL-NP-NEL-DB":"EL-NEL-DB"))throw new InvalidOperationException("Elements replica mismatch.");
    }
    private static void CheckUrl(string value,string host)
    {if(!Uri.TryCreate(value,UriKind.Absolute,out var u)||u.Scheme!="https"||!u.IsDefaultPort||u.Host!=host||u.AbsolutePath!="/"||u.Query.Length!=0||u.Fragment.Length!=0||u.UserInfo.Length!=0)throw new InvalidOperationException("Elements URL mismatch.");}
    private async Task<string> Token(CancellationToken ct)
    {
        if(_token is not null)return _token;
        using var req=new HttpRequestMessage(HttpMethod.Get,Setting("BaseUrl").TrimEnd('/')+"/api/Login/Authenticate");req.Headers.Add("TenantHost",Setting("TenantHost"));
        req.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username")+":"+Setting("Password"))));
        using var resp=await _http.SendAsync(req,ct);resp.EnsureSuccessStatusCode();return _token=JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["data"]?["token"]?.GetValue<string>()??throw new InvalidOperationException("Elements authentication unavailable.");
    }
    private async Task<HttpRequestMessage> Request(HttpMethod method,string path,CancellationToken ct)
    {var req=new HttpRequestMessage(method,Setting("RegistrationBaseUrl").TrimEnd('/')+"/"+path);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await Token(ct));req.Headers.Add("TenantHost",Setting("TenantHost"));req.Headers.Add("Module","Registration");req.Headers.Add("Origin","https://"+Setting("TenantHost"));req.Headers.Referrer=new Uri("https://"+Setting("TenantHost"));return req;}
    private async Task<JsonNode> Read(string path,CancellationToken ct)
    {using var req=await Request(HttpMethod.Get,path,ct);using var resp=await _http.SendAsync(req,ct);resp.EnsureSuccessStatusCode();var body=JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));if(body?["isSuccess"]?.GetValue<bool>()!=true||body["data"] is not JsonNode data)throw new InvalidOperationException("Elements returned incomplete data; no reset was sent.");return data;}
    internal static Mapping ResolveMapping(JsonObject choices)
    {
        var cleared=new List<(int Number,int Id)>();var notCleared=new List<(int Number,int Id)>();
        for(int n=1;n<=3;n++)foreach(var item in choices[$"statusLookup{n}"]?.AsArray()??new JsonArray())
        {
            var label=item?["displayText"]?.GetValue<string>()?.Trim();
            if(string.Equals(label,"Cleared",StringComparison.OrdinalIgnoreCase))cleared.Add((n,item!["uniqueId"]!.GetValue<int>()));
            if(string.Equals(label,"Not Cleared",StringComparison.OrdinalIgnoreCase))notCleared.Add((n,item!["uniqueId"]!.GetValue<int>()));
        }
        if(cleared.Count!=1||notCleared.Count!=1||cleared[0].Number!=notCleared[0].Number||cleared[0].Id<=0||notCleared[0].Id<=0||cleared[0].Id==notCleared[0].Id)throw new InvalidOperationException("Cleared / Not Cleared lookup is missing or ambiguous.");
        return new("uDefLookup"+cleared[0].Number,cleared[0].Number,cleared[0].Id,notCleared[0].Id);
    }
    private async Task<Mapping> MappingAsync(CancellationToken ct)=>ResolveMapping((await Read("api/financial-aid/student-status/user-defined-select-items",ct)).AsObject());
    public async Task<List<ClearanceResetCandidate>> FindCandidatesAsync(CancellationToken ct=default)
    {EnsureEnvironment();return await _find(await MappingAsync(ct),ct);}
    public async Task<ClearanceResetResult> ResetIfEligibleAsync(ClearanceResetCandidate candidate,Func<ClearanceResetAttempt,Task<bool>> recordBeforeWrite,CancellationToken ct=default)
    {
        EnsureRunEnabled();var map=await MappingAsync(ct);var route="api/financial-aid/student-status/user-defined/"+candidate.StatusId;
        ClearanceResetResult Result(string outcome)=>new(candidate.StudentUid,candidate.TermId,candidate.Term,outcome);
        if(!await _eligible(candidate,map,ct))return Result("Skipped: term, clearance or Nelson course records changed.");
        var before=(await Read(route,ct)).AsObject();
        if(before["studentStatusID"]?.GetValue<int>()!=candidate.StatusId)throw new InvalidOperationException("Student term status identity mismatch.");
        if(before[map.Property]?.GetValue<int>()!=map.Cleared)return Result("Skipped: no longer Cleared.");
        // Transcript and unofficial APIs are separate: neither response alone proves zero courses.
        foreach(var path in new[]{"api/academic/register/transcripts/"+candidate.StudentUid,"api/academic/maintenance/unofficial/"+candidate.StudentUid+"/Unofficial"})
            if(HasNelsonCourse((await Read(path,ct)).AsArray(),candidate))return Result("Skipped: a live Nelson course exists for this term.");
        var current=(await Read(route,ct)).AsObject();if(!JsonNode.DeepEquals(before,current))return Result("Skipped: term fields changed during verification.");
        if(!await _eligible(candidate,map,ct))return Result("Skipped: source records changed during verification.");
        var desired=ThesisElementsBillClearanceService.BuildUpdate(current,candidate.StatusId,map.Property,map.NotCleared);
        if(!await recordBeforeWrite(new(candidate,before.ToJsonString(),desired.ToJsonString())))return Result("Skipped: automatic resets were turned off.");
        using var patch=await Request(HttpMethod.Patch,"api/financial-aid/student-status/user-defined",ct);patch.Content=new StringContent(desired.ToJsonString(),Encoding.UTF8,"application/json");
        try
        {
            using var response=await _http.SendAsync(patch,ct);response.EnsureSuccessStatusCode();
            if(JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Elements did not confirm the reset.");
            var after=(await Read(route,ct)).AsObject();foreach(var field in desired)if(!JsonNode.DeepEquals(after[field.Key],field.Value))throw new InvalidOperationException("Reset readback differs from the requested fields.");
        }
        catch(Exception ex){throw new ClearanceResetUnconfirmedException(ex);}
        return Result("Verified Not Cleared");
    }
    internal static bool HasNelsonCourse(JsonArray rows,ClearanceResetCandidate c)
    {
        foreach(var row in rows)
        {
            if(row is not JsonObject obj||obj["studentUID"]?.GetValue<int>()!=c.StudentUid||obj["termCalendarID"] is null||obj["collegeID"] is null||obj["categoryID"] is null)throw new InvalidOperationException("Course response identity or classification is incomplete.");
            if(obj["termCalendarID"]!.GetValue<int>()==c.TermId&&obj["collegeID"]!.GetValue<int>()==0&&obj["categoryID"]!.GetValue<int>()!=c.TransferCategoryId)return true;
        }
        return false;
    }
    private async Task<SqlConnection> Open(CancellationToken ct)
    {var c=new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
    private static async Task<(string Boundary,int Transfer)> Scope(SqlConnection c,CancellationToken ct)
    {
        async Task<T> Unique<T>(string sql)
        {await using var q=new SqlCommand(sql,c){CommandTimeout=30};await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)||r.IsDBNull(0))throw new InvalidOperationException("Clearance reset scope is missing.");var value=(T)r.GetValue(0);if(await r.ReadAsync(ct))throw new InvalidOperationException("Clearance reset scope is ambiguous.");return value;}
        var boundary=(await Unique<string>("SELECT Term FROM dbo.TermCalendar WHERE LTRIM(RTRIM(TextTerm))='Spring 2027'")).Trim();
        var transfer=await Unique<int>("SELECT UniqueID FROM dbo.Glossary_AcadCrsType WHERE LTRIM(RTRIM(DisplayText))='Transfer'");
        if(boundary.Length==0||transfer<=0)throw new InvalidOperationException("Invalid clearance reset scope.");return (boundary,transfer);
    }
    internal const string CandidateSql="""
        SELECT S.StudentUID,S.TermCalendarID,S.StudentStatusID,T.TextTerm,T.Term,ST.StudentID,ST.LastName+', '+ST.FirstName
        FROM dbo.StudentStatus S
        JOIN dbo.Student ST ON ST.StudentUID=S.StudentUID
        JOIN dbo.TermCalendar T ON T.TermCalendarID=S.TermCalendarID
        JOIN dbo.CAMS_StudentStatusUserDefined_View U ON U.StudentStatusID=S.StudentStatusID
        WHERE T.Term >= @Boundary
          AND CASE @Lookup WHEN 1 THEN U.UDefLookup1 WHEN 2 THEN U.UDefLookup2 WHEN 3 THEN U.uDefLookup3 END=@Cleared
          AND NOT EXISTS(SELECT 1 FROM dbo.StudentStatus D WHERE D.StudentUID=S.StudentUID AND D.TermCalendarID=S.TermCalendarID AND D.StudentStatusID<>S.StudentStatusID)
          AND (SELECT COUNT(*) FROM dbo.CAMS_StudentStatusUserDefined_View D WHERE D.StudentStatusID=S.StudentStatusID)=1
          AND NOT EXISTS(SELECT 1 FROM dbo.SRAcademic A WHERE A.StudentUID=S.StudentUID AND A.TermCalendarID=S.TermCalendarID
            AND (A.CollegeID IS NULL OR (A.CollegeID=0 AND (A.CategoryID IS NULL OR A.CategoryID<>@Transfer))))
          AND (@Student IS NULL OR (S.StudentUID=@Student AND S.TermCalendarID=@Term AND S.StudentStatusID=@Status))
        ORDER BY T.Term,S.StudentUID,S.StudentStatusID;
        """;
    private static SqlCommand Command(SqlConnection c,Mapping m,(string Boundary,int Transfer) scope,ClearanceResetCandidate? candidate)
    {
        var q=new SqlCommand(CandidateSql,c){CommandTimeout=120};q.Parameters.Add("@Boundary",SqlDbType.VarChar,30).Value=scope.Boundary;q.Parameters.Add("@Transfer",SqlDbType.Int).Value=scope.Transfer;
        q.Parameters.Add("@Lookup",SqlDbType.Int).Value=m.LookupNumber;q.Parameters.Add("@Cleared",SqlDbType.Int).Value=m.Cleared;
        q.Parameters.Add("@Student",SqlDbType.Int).Value=(object?)candidate?.StudentUid??DBNull.Value;q.Parameters.Add("@Term",SqlDbType.Int).Value=(object?)candidate?.TermId??DBNull.Value;q.Parameters.Add("@Status",SqlDbType.Int).Value=(object?)candidate?.StatusId??DBNull.Value;return q;
    }
    private async Task<List<ClearanceResetCandidate>> FindAsync(Mapping m,CancellationToken ct)
    {await using var c=await Open(ct);var scope=await Scope(c,ct);await using var q=Command(c,m,scope,null);await using var reader=await q.ExecuteReaderAsync(ct);var list=new List<ClearanceResetCandidate>();while(await reader.ReadAsync(ct))list.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3).Trim(),reader.GetString(4).Trim(),scope.Transfer,reader.GetString(5).Trim(),reader.GetString(6).Trim()));return list;}
    private async Task<bool> EligibleAsync(ClearanceResetCandidate candidate,Mapping m,CancellationToken ct)
    {await using var c=await Open(ct);var scope=await Scope(c,ct);if(candidate.TransferCategoryId!=scope.Transfer)return false;await using var q=Command(c,m,scope,candidate);await using var reader=await q.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return false;var matches=reader.GetString(4).Trim()==candidate.TermSequence&&reader.GetString(3).Trim()==candidate.Term;return matches&&!await reader.ReadAsync(ct);}
}
