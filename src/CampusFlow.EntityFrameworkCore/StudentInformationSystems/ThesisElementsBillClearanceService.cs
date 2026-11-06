using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsBillClearanceService : IStudentInformationSystemBillClearanceService
{
    private const string Prefix="StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly HttpClient SharedClient=new(){Timeout=TimeSpan.FromSeconds(90)};
    private readonly IConfiguration _configuration;private readonly HttpClient _client;
    private readonly Func<int,int,CancellationToken,Task<int>> _statusId;
    public StudentInformationSystemProvider Provider=>StudentInformationSystemProvider.ThesisElements;
    public ThesisElementsBillClearanceService(IConfiguration configuration)
    { _configuration=configuration;_client=SharedClient;_statusId=FindStatusAsync; }
    internal ThesisElementsBillClearanceService(IConfiguration configuration,HttpClient client,Func<int,int,CancellationToken,Task<int>> statusId)
    { _configuration=configuration;_client=client;_statusId=statusId; }
    public async Task MarkClearedAsync(string externalStudentId,string externalTermId,CancellationToken cancellationToken=default)
    {
        if(!_configuration.GetValue<bool>("BillApproval:ClearedStatus:Enabled"))throw new InvalidOperationException("Cleared-status updates are not enabled.");
        if(!int.TryParse(externalStudentId,out var student)||student<=0||!int.TryParse(externalTermId,out var term)||term<=0)
            throw new ArgumentException("Valid student and term identifiers are required.");
        var authRoot=new Uri(Setting("BaseUrl").TrimEnd('/')+"/");
        var root=new Uri((_configuration[Prefix+"RegistrationBaseUrl"] ?? Setting("BaseUrl")).TrimEnd('/')+"/");
        if(authRoot.Scheme!="https"||!authRoot.IsDefaultPort||authRoot.AbsolutePath!="/"||authRoot.Query.Length!=0||authRoot.Fragment.Length!=0||authRoot.UserInfo.Length!=0||root.Scheme!="https"||!root.IsDefaultPort||root.AbsolutePath!="/"||root.Query.Length!=0||root.Fragment.Length!=0||root.UserInfo.Length!=0)
            throw new InvalidOperationException("Invalid Elements integration URL.");
        var nonprod=string.Equals(_configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase)||
            string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"],"Staging",StringComparison.OrdinalIgnoreCase);
        if(nonprod&&(root.Host!="np-usc-gateway.thesiscloud.com"||authRoot.Host!="app-elm-nonprod-usc-integrationapi.azurewebsites.net"||Setting("TenantHost")!="el-np-nel.thesiscloud.com"))
            throw new InvalidOperationException("Non-production clearance cannot target production Elements.");
        var statusId=await _statusId(student,term,cancellationToken);
        using var auth=new HttpRequestMessage(HttpMethod.Get,new Uri(authRoot,"api/Login/Authenticate"));
        auth.Headers.Add("TenantHost",Setting("TenantHost"));
        auth.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var authResponse=await _client.SendAsync(auth,cancellationToken);authResponse.EnsureSuccessStatusCode();
        var token=JsonNode.Parse(await authResponse.Content.ReadAsStringAsync(cancellationToken))?["data"]?["token"]?.GetValue<string>()
            ??throw new InvalidOperationException("Elements authentication failed.");
        async Task<JsonObject> Get(string path)
        {
            using var request=Request(HttpMethod.Get,new Uri(root,path),token);using var response=await _client.SendAsync(request,cancellationToken);
            response.EnsureSuccessStatusCode();var body=JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if(body?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Elements did not return the status information.");
            return body["data"]?.AsObject()??throw new InvalidOperationException("Missing status information.");
        }
        var choices=await Get("api/financial-aid/student-status/user-defined-select-items");
        var matches=Enumerable.Range(1,3).SelectMany(n=>(choices[$"statusLookup{n}"]?.AsArray()??new JsonArray())
            .Where(x=>string.Equals(x?["displayText"]?.GetValue<string>()?.Trim(),"Cleared",StringComparison.OrdinalIgnoreCase))
            .Select(x=>(Property:$"uDefLookup{n}",Id:x!["uniqueId"]!.GetValue<int>()))).ToArray();
        if(matches.Length!=1||matches[0].Id<=0)throw new InvalidOperationException("The Cleared status mapping is missing or ambiguous.");
        var route=$"api/financial-aid/student-status/user-defined/{statusId}";
        var current=await Get(route);var update=BuildUpdate(current,statusId,matches[0].Property,matches[0].Id);
        if(JsonNode.DeepEquals(current[matches[0].Property],update[matches[0].Property]))return;
        using var patch=Request(HttpMethod.Patch,new Uri(root,"api/financial-aid/student-status/user-defined"),token);
        patch.Content=new StringContent(update.ToJsonString(),Encoding.UTF8,"application/json");
        using var result=await _client.SendAsync(patch,cancellationToken);result.EnsureSuccessStatusCode();
        if(JsonNode.Parse(await result.Content.ReadAsStringAsync(cancellationToken))?["isSuccess"]?.GetValue<bool>()!=true)
            throw new InvalidOperationException("Elements did not confirm the Cleared status update.");
        var verified=await Get(route);
        foreach(var property in update)
            if(!JsonNode.DeepEquals(verified[property.Key],property.Value))throw new InvalidOperationException("The updated status could not be verified without other field changes.");
    }
    internal static JsonObject BuildUpdate(JsonObject current,int statusId,string property,int clearedId)
    {
        if(current["studentStatusID"]?.GetValue<int>()!=statusId||property is not ("uDefLookup1" or "uDefLookup2" or "uDefLookup3")||clearedId<=0)
            throw new InvalidOperationException("Status identity or mapping does not match the approved term.");
        string[] fields=["studentStatusID","uDefLookup1","uDefLookup2","uDefLookup3","uDefFreeForm1","uDefFreeForm2","uDefFreeForm3",
            "uDefCurr1","uDefCurr2","uDefCurr3","uDefDate1","uDefDate2","uDefDate3","uDefDate4","uDefBit1","uDefBit2","uDefBit3"];
        var result=new JsonObject();
        foreach(var name in fields){if(!current.ContainsKey(name))throw new InvalidOperationException("Elements omitted an existing status field; no update was made.");result[name]=current[name]?.DeepClone();}
        result[property]=clearedId;return result;
    }
    private async Task<int> FindStatusAsync(int student,int term,CancellationToken cancellationToken)
    {
        var cs=_configuration.GetConnectionString("ThesisElementsReadOnly")??throw new InvalidOperationException("The reporting connection is missing.");
        var b=new SqlConnectionStringBuilder(cs);
        if((_configuration["App:EnvironmentLabel"]=="NON-PRODUCTION"||_configuration["ASPNETCORE_ENVIRONMENT"]=="Staging")&&
            (!(string.Equals(b.DataSource,"sql-elm-nonprod-usc-replica.database.windows.net",StringComparison.OrdinalIgnoreCase)||string.Equals(b.DataSource,"tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433",StringComparison.OrdinalIgnoreCase))||b.InitialCatalog!="EL-NP-NEL-DB"))
            throw new InvalidOperationException("The non-production status lookup must use its own replica.");
        await using var connection=new SqlConnection(cs);await connection.OpenAsync(cancellationToken);
        await using var command=new SqlCommand("SELECT StudentStatusID FROM dbo.CAMS_StudentStatus_View WHERE StudentUID=@Student AND TermCalendarID=@Term",connection);
        command.Parameters.Add("@Student",SqlDbType.Int).Value=student;command.Parameters.Add("@Term",SqlDbType.Int).Value=term;
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("No student status exists for this term; none was created.");
        var id=reader.GetInt32(0);if(await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("Multiple student status records exist for this term.");return id;
    }
    private HttpRequestMessage Request(HttpMethod method,Uri uri,string token)
    {var r=new HttpRequestMessage(method,uri);r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);r.Headers.Add("TenantHost",Setting("TenantHost"));r.Headers.Add("Module","Registration");
        var origin="https://"+Setting("TenantHost").TrimEnd('/');r.Headers.Add("Origin",origin);r.Headers.Referrer=new Uri(origin);return r;}
    private string Setting(string name)=>_configuration[Prefix+name]??throw new InvalidOperationException($"Missing Elements setting '{name}'.");
}
