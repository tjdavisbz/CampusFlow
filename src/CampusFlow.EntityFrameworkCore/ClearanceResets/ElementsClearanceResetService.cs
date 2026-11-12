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
    private readonly IClearanceResetProtection _protection;
    private readonly Func<CancellationToken,Task> _verifyFields;
    internal sealed record Mapping(string Property,int LookupNumber,int Cleared,int Blank);
    public ElementsClearanceResetService(IConfiguration configuration,IClearanceResetProtection protection)
    {_configuration=configuration;_http=SharedClient;_find=FindAsync;_eligible=EligibleAsync;_protection=protection;_verifyFields=VerifyResetFieldsAsync;}
    internal ElementsClearanceResetService(IConfiguration configuration,HttpClient http,
        Func<Mapping,CancellationToken,Task<List<ClearanceResetCandidate>>> find,
        Func<ClearanceResetCandidate,Mapping,CancellationToken,Task<bool>> eligible,IClearanceResetProtection protection,
        Func<CancellationToken,Task>? verifyFields=null)
    {_configuration=configuration;_http=http;_find=find;_eligible=eligible;_protection=protection;_verifyFields=verifyFields??(_=>Task.CompletedTask);}
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
        var cleared=new List<(int Number,int Id)>();
        for(int n=1;n<=3;n++)foreach(var item in choices[$"statusLookup{n}"]?.AsArray()??new JsonArray())
        {
            var label=item?["displayText"]?.GetValue<string>()?.Trim();
            if(string.Equals(label,"Cleared",StringComparison.OrdinalIgnoreCase))cleared.Add((n,item!["uniqueId"]!.GetValue<int>()));
        }
        if(cleared.Count!=1||cleared[0].Id<=0)throw new InvalidOperationException("Cleared lookup is missing or ambiguous.");
        var blanks=(choices[$"statusLookup{cleared[0].Number}"]?.AsArray()??new JsonArray()).Where(x=>
            x?["uniqueId"]?.GetValue<int>()==0 && string.IsNullOrWhiteSpace(x["displayText"]?.GetValue<string>())).ToArray();
        if(blanks.Length!=1)throw new InvalidOperationException("The blank clearance option is missing or ambiguous.");
        return new("uDefLookup"+cleared[0].Number,cleared[0].Number,cleared[0].Id,0);
    }
    private async Task<Mapping> MappingAsync(CancellationToken ct)
    {
        var map=ResolveMapping((await Read("api/financial-aid/student-status/user-defined-select-items",ct)).AsObject());
        if(map.Property!="uDefLookup2")throw new InvalidOperationException("The clearance field configuration changed. Review field definitions before resetting.");
        await _verifyFields(ct);return map;
    }
    internal static void ValidateResetFields(IReadOnlyList<(string Property,string Label)> fields)
    {
        (string Property,string Label)[] expected=[("UDefLookup2","Cleared Status"),("uDefDate1","Disable Blackboard"),
            ("uDefBit2","Payment Plan?"),("uDefBit1","Non-Returning"),("uDefFreeForm1","Non-Returning Reason")];
        foreach(var field in expected)
        {
            var matches=fields.Where(x=>string.Equals(x.Property,field.Property,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1||!string.Equals(matches[0].Label.Trim(),field.Label,StringComparison.OrdinalIgnoreCase)||
                fields.Count(x=>string.Equals(x.Label.Trim(),field.Label,StringComparison.OrdinalIgnoreCase))!=1)
                throw new InvalidOperationException("The status field configuration changed. No reset was sent; review the field labels.");
        }
    }
    private async Task VerifyResetFieldsAsync(CancellationToken ct)
    {
        await using var c=await Open(ct);
        await using var q=new SqlCommand("SELECT FieldPhysicalName,FieldDisplayName FROM dbo.CAMSUserDefinedRefFlds WHERE CAMSModuleID=12",c){CommandTimeout=30};
        await using var r=await q.ExecuteReaderAsync(ct);var fields=new List<(string,string)>();
        while(await r.ReadAsync(ct))fields.Add((r.GetString(0).Trim(),r.IsDBNull(1)?"":r.GetString(1).Trim()));
        ValidateResetFields(fields);
    }
    public async Task<List<ClearanceResetCandidate>> FindCandidatesAsync(CancellationToken ct=default)
    {EnsureEnvironment();var candidates=await _find(await MappingAsync(ct),ct);var protectedKeys=await _protection.ProtectedAsync(candidates,ct);return candidates.Where(x=>!protectedKeys.Contains((x.StudentUid,x.TermId))).ToList();}
    public async Task<ClearanceResetResult> ResetIfEligibleAsync(ClearanceResetCandidate candidate,Func<ClearanceResetAttempt,Task<bool>> recordBeforeWrite,CancellationToken ct=default)
    {
        EnsureRunEnabled();await using var lease=await _protection.AcquireAsync(candidate.StudentUid,candidate.TermId,ct);var map=await MappingAsync(ct);var route="api/financial-aid/student-status/user-defined/"+candidate.StatusId;
        ClearanceResetResult Result(string outcome)=>new(candidate.StudentUid,candidate.TermId,candidate.Term,outcome);
        if(await _protection.IsProtectedAsync(candidate,ct))return Result("Skipped: approved bill or manual clearance exception.");
        if(!await _eligible(candidate,map,ct))return Result("Skipped: term or clearance records changed.");
        var before=(await Read(route,ct)).AsObject();
        if(before["studentStatusID"]?.GetValue<int>()!=candidate.StatusId)throw new InvalidOperationException("Student term status identity mismatch.");
        if(before[map.Property]?.GetValue<int>()!=map.Cleared)return Result("Skipped: no longer Cleared.");
        var current=(await Read(route,ct)).AsObject();if(!JsonNode.DeepEquals(before,current))return Result("Skipped: term fields changed during verification.");
        if(!await _eligible(candidate,map,ct))return Result("Skipped: source records changed during verification.");
        var desired=ThesisElementsBillClearanceService.BuildBlankUpdate(current,candidate.StatusId,map.Property,map.Blank);
        // Elements accepts an empty reason but rejects JSON null for this text field.
        desired["uDefDate1"]=null;desired["uDefFreeForm1"]="";
        desired["uDefBit1"]=false;desired["uDefBit2"]=false;
        if(!await recordBeforeWrite(new(candidate,before.ToJsonString(),desired.ToJsonString())))return Result("Skipped: automatic resets were turned off.");
        if(await _protection.IsProtectedAsync(candidate,ct))return Result("Skipped: approval or manual exception changed before dispatch.");
        using var patch=await Request(HttpMethod.Patch,"api/financial-aid/student-status/user-defined",ct);patch.Content=new StringContent(desired.ToJsonString(),Encoding.UTF8,"application/json");
        try
        {
            using var response=await _http.SendAsync(patch,ct);response.EnsureSuccessStatusCode();
            if(JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Elements did not confirm the reset.");
            var after=(await Read(route,ct)).AsObject();foreach(var field in desired)if(!JsonNode.DeepEquals(after[field.Key],field.Value))throw new InvalidOperationException("Reset readback differs from the requested fields.");
        }
        catch(Exception ex){throw new ClearanceResetUnconfirmedException(ex);}
        return Result("Verified blank");
    }
    private async Task<SqlConnection> Open(CancellationToken ct)
    {var c=new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
    private static async Task<string> Scope(SqlConnection c,CancellationToken ct)
    {
        async Task<T> Unique<T>(string sql)
        {await using var q=new SqlCommand(sql,c){CommandTimeout=30};await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)||r.IsDBNull(0))throw new InvalidOperationException("Clearance reset scope is missing.");var value=(T)r.GetValue(0);if(await r.ReadAsync(ct))throw new InvalidOperationException("Clearance reset scope is ambiguous.");return value;}
        var boundary=(await Unique<string>("SELECT Term FROM dbo.TermCalendar WHERE LTRIM(RTRIM(TextTerm))='Fall 2026'")).Trim();
        if(boundary.Length==0)throw new InvalidOperationException("Invalid clearance reset scope.");return boundary;
    }
    internal const string CandidateSql="""
        SELECT S.StudentUID,S.TermCalendarID,S.StudentStatusID,T.TextTerm,T.Term,ST.StudentID,ST.LastName+', '+ST.FirstName
        FROM dbo.StudentStatus S
        JOIN dbo.Student ST ON ST.StudentUID=S.StudentUID
        JOIN dbo.TermCalendar T ON T.TermCalendarID=S.TermCalendarID
        JOIN dbo.CAMS_StudentStatusUserDefined_View U ON U.StudentStatusID=S.StudentStatusID
        WHERE T.Term >= @Boundary
          AND (@RequireCleared=0 OR CASE @Lookup WHEN 1 THEN U.UDefLookup1 WHEN 2 THEN U.UDefLookup2 WHEN 3 THEN U.uDefLookup3 END=@Cleared)
          AND NOT EXISTS(SELECT 1 FROM dbo.StudentStatus D WHERE D.StudentUID=S.StudentUID AND D.TermCalendarID=S.TermCalendarID AND D.StudentStatusID<>S.StudentStatusID)
          AND (SELECT COUNT(*) FROM dbo.CAMS_StudentStatusUserDefined_View D WHERE D.StudentStatusID=S.StudentStatusID)=1
          AND (@Student IS NULL OR (S.StudentUID=@Student AND S.TermCalendarID=@Term AND S.StudentStatusID=@Status))
        ORDER BY T.Term,S.StudentUID,S.StudentStatusID;
        """;
    private static SqlCommand Command(SqlConnection c,Mapping m,string scope,ClearanceResetCandidate? candidate,bool requireCleared=true)
    {
        var q=new SqlCommand(CandidateSql,c){CommandTimeout=120};q.Parameters.Add("@Boundary",SqlDbType.VarChar,30).Value=scope;q.Parameters.Add("@RequireCleared",SqlDbType.Bit).Value=requireCleared;
        q.Parameters.Add("@Lookup",SqlDbType.Int).Value=m.LookupNumber;q.Parameters.Add("@Cleared",SqlDbType.Int).Value=m.Cleared;
        q.Parameters.Add("@Student",SqlDbType.Int).Value=(object?)candidate?.StudentUid??DBNull.Value;q.Parameters.Add("@Term",SqlDbType.Int).Value=(object?)candidate?.TermId??DBNull.Value;q.Parameters.Add("@Status",SqlDbType.Int).Value=(object?)candidate?.StatusId??DBNull.Value;return q;
    }
    private async Task<List<ClearanceResetCandidate>> FindAsync(Mapping m,CancellationToken ct)
    {await using var c=await Open(ct);var scope=await Scope(c,ct);await using var q=Command(c,m,scope,null);await using var reader=await q.ExecuteReaderAsync(ct);var list=new List<ClearanceResetCandidate>();while(await reader.ReadAsync(ct))list.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3).Trim(),reader.GetString(4).Trim(),0,reader.GetString(5).Trim(),reader.GetString(6).Trim()));return list;}
    public async Task<List<ClearanceResetTerm>> GetTermsAsync(CancellationToken ct=default)
    {
        EnsureEnvironment();await using var c=await Open(ct);var boundary=await Scope(c,ct);
        await using var q=new SqlCommand("SELECT TermCalendarID,TextTerm FROM dbo.TermCalendar WHERE Term>=@Boundary ORDER BY Term,TermCalendarID",c){CommandTimeout=30};q.Parameters.Add("@Boundary",SqlDbType.VarChar,30).Value=boundary;
        await using var reader=await q.ExecuteReaderAsync(ct);var terms=new List<ClearanceResetTerm>();while(await reader.ReadAsync(ct))terms.Add(new(reader.GetInt32(0),reader.GetString(1).Trim()));return terms;
    }
    public async Task<ClearanceResetCandidate> GetTargetAsync(int studentUid,int termId,CancellationToken ct=default)
    {
        EnsureEnvironment();if(studentUid<=0||termId<=0)throw new ArgumentException("A student and term are required.");
        await using var c=await Open(ct);var scope=await Scope(c,ct);var filter=new ClearanceResetCandidate(studentUid,termId,0,"","",0);
        var sql=CandidateSql.Replace(" AND S.StudentStatusID=@Status","");
        await using var q=Command(c,new Mapping("uDefLookup2",2,1,0),scope,filter,false);q.CommandText=sql;
        await using var reader=await q.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new InvalidOperationException("No eligible unique student term status was found.");
        var result=new ClearanceResetCandidate(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3).Trim(),reader.GetString(4).Trim(),0,reader.GetString(5).Trim(),reader.GetString(6).Trim());
        if(await reader.ReadAsync(ct))throw new InvalidOperationException("The student term status is ambiguous.");return result;
    }
    private async Task<bool> EligibleAsync(ClearanceResetCandidate candidate,Mapping m,CancellationToken ct)
    {await using var c=await Open(ct);var scope=await Scope(c,ct);await using var q=Command(c,m,scope,candidate);await using var reader=await q.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return false;var matches=reader.GetString(4).Trim()==candidate.TermSequence&&reader.GetString(3).Trim()==candidate.Term;return matches&&!await reader.ReadAsync(ct);}
}
