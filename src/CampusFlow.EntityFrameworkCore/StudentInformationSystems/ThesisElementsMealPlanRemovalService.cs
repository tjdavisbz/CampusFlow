using System;
using System.Collections.Generic;
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

public sealed class ThesisElementsMealPlanRemovalService : IStudentInformationSystemMealPlanRemovalService
{
    private const string Prefix="StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly HttpClient SharedClient=new(){Timeout=TimeSpan.FromSeconds(90)};
    private readonly IConfiguration _configuration;private readonly IElementsHousingAssignmentWriter _environment;private readonly HttpClient _client;
    private readonly Func<int,CancellationToken,Task<IReadOnlyList<StudentMealPlanAssignment>>> _lookup;
    public ThesisElementsMealPlanRemovalService(IConfiguration configuration,IElementsHousingAssignmentWriter environment)
    {_configuration=configuration;_environment=environment;_client=SharedClient;_lookup=LookupAsync;}
    internal ThesisElementsMealPlanRemovalService(IConfiguration configuration,IElementsHousingAssignmentWriter environment,HttpClient client,
        Func<int,CancellationToken,Task<IReadOnlyList<StudentMealPlanAssignment>>> lookup)
    {_configuration=configuration;_environment=environment;_client=client;_lookup=lookup;}
    public bool IsEnabled
    {
        get {if(!_configuration.GetValue<bool>("MealPlans:EnableAdministrativeRemoval"))return false;try{_environment.EnsureWriteEnabled();return true;}catch(InvalidOperationException){return false;}}
    }
    private string Setting(string key)=>_configuration[Prefix+key]??throw new InvalidOperationException("Elements meal-plan integration is not configured.");
    private Uri Root()
    {
        var np=string.Equals(_configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase)||string.Equals(_configuration["ASPNETCORE_ENVIRONMENT"],"Staging",StringComparison.OrdinalIgnoreCase);
        var uri=new Uri(Setting("BaseUrl").TrimEnd('/')+"/");
        if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.AbsolutePath!="/"||uri.Query.Length!=0||uri.Fragment.Length!=0||uri.UserInfo.Length!=0||
            uri.Host!=(np?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"app-elm-prod-usc-integrationapi.azurewebsites.net")||
            Setting("TenantHost")!=(np?"el-np-nel.thesiscloud.com":"el-nel.thesiscloud.com"))throw new InvalidOperationException("The meal-plan environment does not match this portal.");
        return uri;
    }
    private HttpRequestMessage Request(HttpMethod method,string route,string? token=null)
    {var r=new HttpRequestMessage(method,new Uri(Root(),route));r.Headers.Add("TenantHost",Setting("TenantHost"));r.Headers.Add("Module","StudentLife");if(token is not null)r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return r;}
    private async Task<string> Token(CancellationToken ct)
    {
        using var r=Request(HttpMethod.Get,"api/Login/Authenticate");r.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username")+":"+Setting("Password"))));
        using var result=await _client.SendAsync(r,ct);result.EnsureSuccessStatusCode();
        return JsonNode.Parse(await result.Content.ReadAsStringAsync(ct))?["data"]?["token"]?.GetValue<string>()??throw new InvalidOperationException("Elements did not authenticate meal-plan access.");
    }
    private async Task<JsonArray> Live(int student,string token,CancellationToken ct)
    {
        using var r=Request(HttpMethod.Get,$"api/student-life/housing/meal-plan/get-assigned-meal-plan/{student}",token);
        using var result=await _client.SendAsync(r,ct);result.EnsureSuccessStatusCode();var root=JsonNode.Parse(await result.Content.ReadAsStringAsync(ct));
        if(root?["isSuccess"]?.GetValue<bool>()!=true||root["data"] is not JsonArray rows)throw new InvalidOperationException("Elements did not return the live meal assignments.");
        return rows;
    }
    private static int Id(JsonObject row,string key)
    {
        var matches=row.Where(x=>string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase)).ToList();
        if(matches.Count!=1||matches[0].Value is not JsonValue value||!value.TryGetValue<int>(out var id)||id<=0)throw new InvalidOperationException("Elements returned an unsupported meal-assignment identity. No removal was made.");return id;
    }
    public async Task<IReadOnlyList<StudentMealPlanAssignment>> GetAssignmentsAsync(string externalStudentId,CancellationToken cancellationToken=default)
    {
        if(!int.TryParse(externalStudentId,out var student)||student<=0)throw new ArgumentException("Invalid student identifier.");
        var rows=await Live(student,await Token(cancellationToken),cancellationToken);
        if(rows.Count==0)return Array.Empty<StudentMealPlanAssignment>();
        var catalog=await _lookup(student,cancellationToken);var result=new List<StudentMealPlanAssignment>();
        foreach(var node in rows)
        {
            if(node is not JsonObject row)throw new InvalidOperationException("Invalid meal-assignment response.");
            var assignment=Id(row,"assignmentID");
            var hasMealId=row.Any(x=>string.Equals(x.Key,"mealPlanID",StringComparison.OrdinalIgnoreCase));
            var meal=hasMealId?Id(row,"mealPlanID"):(int?)null;
            if(row.Any(x=>string.Equals(x.Key,"studentUID",StringComparison.OrdinalIgnoreCase))&&Id(row,"studentUID")!=student)throw new InvalidOperationException("The meal assignment belongs to another student.");
            var matches=catalog.Where(x=>x.AssignmentId==assignment&&(!meal.HasValue||x.MealPlanId==meal.Value)).ToList();
            if(matches.Count!=1||matches[0].TermCalendarId<=0)throw new InvalidOperationException("The live meal assignment and reporting term do not match. Retry after the reporting data refreshes.");
            if(!hasMealId)
            {
                // The live API returns the assignment ID, name and dates, but no meal-plan ID.
                // Resolve the plan/term only through this student's matching replica row.
                var name=row["mealPlanName"]?.GetValue<string>();
                if(!matches[0].StartDate.HasValue||!matches[0].EndDate.HasValue||
                    !DateTime.TryParse(row["startDate"]?.GetValue<string>(),out var start)||!DateTime.TryParse(row["endDate"]?.GetValue<string>(),out var end)||
                    !string.Equals(name?.Trim(),matches[0].Name.Trim(),StringComparison.OrdinalIgnoreCase)||
                    start.Date!=matches[0].StartDate.Value.Date||end.Date!=matches[0].EndDate.Value.Date)
                    throw new InvalidOperationException("The live meal-plan name/dates and reporting term do not match. No removal was made.");
            }
            if(result.Any(x=>x.AssignmentId==assignment))throw new InvalidOperationException("Duplicate meal assignment identity.");result.Add(matches[0]);
        }
        return result;
    }
    public async Task RemoveAsync(string externalStudentId,int termId,int assignmentId,int mealPlanId,CancellationToken cancellationToken=default)
    {
        if(!IsEnabled)throw new InvalidOperationException("Administrative meal-plan removal is not enabled.");
        if(termId<=0||assignmentId<=0||mealPlanId<=0)throw new ArgumentException("Review one student, term and assignment before removal.");
        var matches=(await GetAssignmentsAsync(externalStudentId,cancellationToken)).Where(x=>x.AssignmentId==assignmentId&&x.MealPlanId==mealPlanId&&x.TermCalendarId==termId).ToList();
        if(matches.Count!=1)throw new InvalidOperationException("This meal assignment changed or belongs to another term. Refresh before removing it.");
        var token=await Token(cancellationToken);
        using var request=Request(HttpMethod.Delete,$"api/student-life/housing/meal-plan/delete-student-assignment/{int.Parse(externalStudentId)}/{assignmentId}",token);
        using var result=await _client.SendAsync(request,cancellationToken);result.EnsureSuccessStatusCode();
        if(JsonNode.Parse(await result.Content.ReadAsStringAsync(cancellationToken))?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException("Elements did not confirm removal. Review the assignment before retrying.");
        var after=await Live(int.Parse(externalStudentId),token,cancellationToken);
        if(after.Any(x=>x is not JsonObject || Id((JsonObject)x,"assignmentID")==assignmentId))throw new InvalidOperationException("The removed meal assignment is still present. Check Elements before retrying.");
    }
    private async Task<IReadOnlyList<StudentMealPlanAssignment>> LookupAsync(int student,CancellationToken ct)
    {
        var cs=_configuration.GetConnectionString("ThesisElementsReadOnly")??throw new InvalidOperationException("Missing meal-plan reporting connection.");
        var b=new SqlConnectionStringBuilder(cs);var np=Root().Host.Contains("nonprod",StringComparison.Ordinal);
        var host=np?"sql-elm-nonprod-usc-replica.database.windows.net":"sql-elm-prod-usc-replica.database.windows.net";
        if(!(string.Equals(b.DataSource,host,StringComparison.OrdinalIgnoreCase)||string.Equals(b.DataSource,$"tcp:{host},1433",StringComparison.OrdinalIgnoreCase))||b.InitialCatalog!=(np?"EL-NP-NEL-DB":"EL-NEL-DB"))throw new InvalidOperationException("The meal-plan reporting environment does not match.");
        await using var c=new SqlConnection(cs);await c.OpenAsync(ct);await using var command=new SqlCommand("""
            SELECT a.AssignmentID,m.MealPlanID,m.TermCalendarID,m.MealPlanName,m.Amount,m.StartDate,m.EndDate
            FROM StudentLife.MealPlanStudentAssignments a JOIN StudentLife.MealPlans m ON m.MealPlanID=a.MealPlanID
            WHERE a.StudentUID=@Student;
            """,c);command.Parameters.AddWithValue("@Student",student);await using var reader=await command.ExecuteReaderAsync(ct);var rows=new List<StudentMealPlanAssignment>();
        while(await reader.ReadAsync(ct))rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2)?0:reader.GetInt32(2),reader.GetString(3).Trim(),reader.IsDBNull(4)?null:Convert.ToDecimal(reader.GetValue(4)),reader.GetDateTime(5),reader.GetDateTime(6)));return rows;
    }
}
