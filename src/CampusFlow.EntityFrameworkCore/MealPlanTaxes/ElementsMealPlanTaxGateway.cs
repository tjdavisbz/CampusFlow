using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
namespace CampusFlow.MealPlanTaxes;
public sealed class ElementsMealPlanTaxGateway:IElementsMealPlanTaxGateway,ITransientDependency
{
    private static readonly HttpClient SharedClient=new();
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly IStudentInformationSystemMealPlanRemovalService _assignments;
    private readonly IElementsMealPlanTaxReadModel _read;
    private readonly HttpClient _client;
    public ElementsMealPlanTaxGateway(IConfiguration config,IMemoryCache cache,IStudentInformationSystemMealPlanRemovalService assignments,IElementsMealPlanTaxReadModel read):this(config,cache,assignments,read,SharedClient){}
    internal ElementsMealPlanTaxGateway(IConfiguration config,IMemoryCache cache,IStudentInformationSystemMealPlanRemovalService assignments,IElementsMealPlanTaxReadModel read,HttpClient client)
    {_config=config;_cache=cache;_assignments=assignments;_read=read;_client=client;}
    public async Task<IReadOnlyList<MealPlanTaxTarget>> TargetsAsync(int studentUid,int termId,CancellationToken ct=default)
    {
        MealPlanTaxEnvironment.Validate(_config);
        if(studentUid<=0||termId<=0)throw new ArgumentException("A valid tax student and term are required.");
        if(!await _read.IsInScopeAsync(termId,ct))return [];
        var targets=new List<MealPlanTaxTarget>();
        foreach(var row in (await _assignments.GetAssignmentsAsync(studentUid.ToString(CultureInfo.InvariantCulture),ct)).Where(x=>x.TermCalendarId==termId))
        {
            if(row.Amount is not decimal amount||amount<0)throw new InvalidOperationException("The assigned meal-plan base price could not be verified.");
            if(MealPlanTaxRules.Amount(amount)==0)continue;
            var target=new MealPlanTaxTarget(studentUid,termId,row.AssignmentId,row.MealPlanId,amount);target.Validate();
            if(targets.Any(x=>x.AssignmentId==row.AssignmentId))throw new InvalidOperationException("Duplicate meal assignments require review.");targets.Add(target);
        }
        if(targets.Count>0)await _read.DestinationAsync(ct);
        return targets;
    }
    public async Task<string?> FindAsync(MealPlanTaxTarget target,CancellationToken ct=default)
    {
        MealPlanTaxEnvironment.Validate(_config);target.Validate();
        var destination=await _read.DestinationAsync(ct);var reported=await _read.ReadAsync(target,destination,ct);
        if(reported.PostedId is not null)return reported.PostedId;
        var token=await Token(ct);
        using var request=Request(HttpMethod.Get,$"/api/billing/cashier-entry/batches/batch-entries-list/{target.StudentUid}/{destination.BatchId}",token);
        using var response=await _client.SendAsync(request,ct);var root=await Read(response,ct);
        if(root["data"] is not JsonObject data || !data.TryGetPropertyValue("batchEntriesList",out var list) || list is not null and not JsonArray)
            throw new InvalidOperationException("Elements returned an unsupported meal-tax batch list.");
        var rows=list as JsonArray ?? new JsonArray();
        if(rows.Any(x=>x is not JsonObject))throw new InvalidOperationException("Elements returned an invalid tax entry.");
        var matches=new List<string>();
        foreach(var row in rows.OfType<JsonObject>())
        {
            var id=Int(row,"billingBatchID");
            if(id<=0||Int(row,"batchMasterID")!=destination.BatchId||Int(row,"studentUID")!=target.StudentUid)
                throw new InvalidOperationException("Elements returned a tax entry outside the requested student or batch.");
            var reference=row["refNo"]?.GetValue<string>()?.Trim()??"";
            if(reference!=target.Reference)continue;
            if(Int(row,"termCalendarID")!=target.TermId||Int(row,"transDOCID")!=destination.DocumentId||
                !decimal.TryParse(row["amount"]?.ToString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)||amount!=target.TaxAmount)
                throw new InvalidOperationException("The existing tax reference has a different term, document or amount.");
            matches.Add("Batch:"+id);
        }
        if(matches.Count>1)throw new InvalidOperationException("Duplicate meal-tax references require billing review.");
        if(matches.Count==0&&reported.BatchId is not null)throw new InvalidOperationException("The reporting tax row is missing from the live batch.");
        return matches.SingleOrDefault();
    }
    public async Task CreateAsync(MealPlanTaxTarget target,CancellationToken ct=default)
    {
        target.Validate();
        if(!(await TargetsAsync(target.StudentUid,target.TermId,ct)).Contains(target))
            throw new InvalidOperationException("The live meal assignment or base price changed before posting tax.");
        if(await FindAsync(target,ct) is not null)return;
        var destination=await _read.DestinationAsync(ct);var token=await Token(ct);
        using var request=Request(HttpMethod.Post,"/api/billing/batch/student/batch",token);
        var date=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")).Date;
        request.Content=new StringContent(JsonSerializer.Serialize(new{
            batchMasterID=destination.BatchId,billingBatchID=0,allClear="",studentID=target.StudentUid,termID=target.TermId,
            transactionTypeID="DEBIT",currencyTypeID=(int?)null,extendedDocID=0,transDocID=destination.DocumentId,
            transactionDate=date,amount=target.TaxAmount,currencyTypeAmt=(decimal?)null,arTypeID=destination.ArTypeId,
            description="Meal plan sales tax (8.25%)",paymentPlanID=0,refNo=target.Reference
        }),Encoding.UTF8,"application/json");
        // One request only. The coordinator persisted the attempt before this call.
        using var response=await _client.SendAsync(request,ct);var root=await Read(response,ct);
        if(root["data"] is not JsonValue value||!value.TryGetValue<int>(out var id)||id<=0)
            throw new InvalidOperationException("Elements did not confirm creation of the meal-tax charge.");
    }
    public async Task ConfirmReportingAsync(MealPlanTaxTarget target,CancellationToken ct=default)
    {
        var destination=await _read.DestinationAsync(ct);
        for(var attempt=0;attempt<4;attempt++)
        {
            var row=await _read.ReadAsync(target,destination,ct);
            if(row.PostedId is not null||row.BatchId is not null)return;
            if(attempt<3)await Task.Delay(1000,ct);
        }
        throw new InvalidOperationException("The tax charge is not yet visible in the bill reporting data.");
    }
    private string Setting(string key)=>_config[MealPlanTaxEnvironment.ApiPath+key]??throw new InvalidOperationException("A required meal-tax API setting is missing.");
    private async Task<string> Token(CancellationToken ct)
    {
        MealPlanTaxEnvironment.Validate(_config);var key="MealPlanTax:"+Setting("TenantHost");
        if(_cache.TryGetValue(key,out string? cached)&&!string.IsNullOrWhiteSpace(cached))return cached;
        using var request=new HttpRequestMessage(HttpMethod.Get,Setting("BaseUrl").TrimEnd('/')+"/api/Login/Authenticate");
        request.Headers.Add("TenantHost",Setting("TenantHost"));request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username")+":"+Setting("Password"))));
        using var response=await _client.SendAsync(request,ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Meal-tax API authentication failed.");
        var token=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["data"]?["token"]?.GetValue<string>()??throw new InvalidOperationException("Elements returned no meal-tax token.");
        _cache.Set(key,token,TimeSpan.FromMinutes(10));return token;
    }
    private HttpRequestMessage Request(HttpMethod method,string route,string token)
    {
        MealPlanTaxEnvironment.Validate(_config);var request=new HttpRequestMessage(method,Setting("RegistrationBaseUrl").TrimEnd('/')+route);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Headers.Add("Module","Billing");request.Headers.Add("TenantHost",Setting("TenantHost"));request.Headers.Add("Origin","https://"+Setting("TenantHost"));request.Headers.Referrer=new Uri("https://"+Setting("TenantHost"));return request;
    }
    private static int Int(JsonObject row,string name)=>row[name]?.GetValue<int>()??0;
    private static async Task<JsonObject> Read(HttpResponseMessage response,CancellationToken ct)
    {
        var text=await response.Content.ReadAsStringAsync(ct);JsonObject? root;
        try{root=JsonNode.Parse(text) as JsonObject;}catch(JsonException){root=null;}
        if(!response.IsSuccessStatusCode||root?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException($"Elements rejected the meal-tax request (HTTP {(int)response.StatusCode}).");return root;
    }
}
