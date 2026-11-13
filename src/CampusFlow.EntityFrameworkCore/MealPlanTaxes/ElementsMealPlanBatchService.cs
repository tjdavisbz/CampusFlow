using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.MealPlanTaxes;

public interface IElementsMealPlanBatchService
{
    Task AfterAssignmentAsync(int studentUid,int mealPlanId,CancellationToken ct=default);
    Task EnsureAsync(MealPlanTaxTarget target,CancellationToken ct=default);
}

// Moves the existing entry ID; never copies, creates, deletes or posts a charge.
// A lost PUT response is safe to reconcile by rereading that same ID on the next review.
public sealed class ElementsMealPlanBatchService : IElementsMealPlanBatchService,ITransientDependency
{
    private static readonly HttpClient Shared=new(){Timeout=TimeSpan.FromSeconds(60)};
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly IStudentInformationSystemMealPlanRemovalService _assignments;
    private readonly IElementsMealPlanTaxReadModel _tax;
    private readonly IElementsMealPlanBatchReadModel _read;
    private readonly HttpClient _http;
    public ElementsMealPlanBatchService(IConfiguration config,IMemoryCache cache,IStudentInformationSystemMealPlanRemovalService assignments,
        IElementsMealPlanTaxReadModel tax,IElementsMealPlanBatchReadModel read):this(config,cache,assignments,tax,read,Shared){}
    internal ElementsMealPlanBatchService(IConfiguration config,IMemoryCache cache,IStudentInformationSystemMealPlanRemovalService assignments,
        IElementsMealPlanTaxReadModel tax,IElementsMealPlanBatchReadModel read,HttpClient http)
    {_config=config;_cache=cache;_assignments=assignments;_tax=tax;_read=read;_http=http;}
    private bool Enabled=>_config.GetValue<int>("MealPlans:SharedBatchMasterId")>0;
    public async Task AfterAssignmentAsync(int studentUid,int mealPlanId,CancellationToken ct=default)
    {
        if(!Enabled)return;
        MealPlanTaxEnvironment.Validate(_config);
        var assigned=(await _assignments.GetAssignmentsAsync(studentUid.ToString(CultureInfo.InvariantCulture),ct)).Where(x=>x.MealPlanId==mealPlanId).ToList();
        if(assigned.Count!=1 || assigned[0].Amount is not decimal amount || amount<0)
            throw new InvalidOperationException("The meal assignment must be confirmed before batching.");
        if(amount==0)return;
        var row=assigned[0]; await EnsureAsync(new(studentUid,row.TermCalendarId,row.AssignmentId,row.MealPlanId,amount),ct);
    }
    public async Task EnsureAsync(MealPlanTaxTarget target,CancellationToken ct=default)
    {
        if(!Enabled)return;
        MealPlanTaxEnvironment.Validate(_config);target.Validate();
        var paid=(await _assignments.GetAssignmentsAsync(target.StudentUid.ToString(CultureInfo.InvariantCulture),ct))
            .Where(x=>x.TermCalendarId==target.TermId && x.Amount is >0).ToList();
        if(paid.Count!=1 || paid[0].AssignmentId!=target.AssignmentId || paid[0].MealPlanId!=target.MealPlanId || paid[0].Amount!=target.BaseAmount)
            throw new InvalidOperationException("Review the current meal assignment before batching.");
        var destination=await _tax.DestinationAsync(ct);
        if(destination.SharedBatchId<=0 || destination.BatchId!=destination.SharedBatchId)
            throw new InvalidOperationException("The meal-plan and tax destinations must be the same shared batch.");
        var snapshot=await _read.ReadAsync(target,destination,ct);var token=await Token(ct);
        var live=await Live(target,snapshot,token,ct);
        RequireMatches(snapshot,live);
        // Validate every proposed body before the first mutation.
        var moves=snapshot.Entries.Where(x=>x.BatchId!=destination.BatchId)
            .Select(x=>(Entry:x,Body:MoveBody(live.Single(n=>Int(n,"billingBatchID")==x.Id),target,x,destination.BatchId))).ToList();
        foreach(var move in moves)
        {
            if(await _tax.DestinationAsync(ct)!=destination)
                throw new InvalidOperationException("The shared meal-plan destination changed before moving a charge.");
            var current=await _read.ReadAsync(target,destination,ct);
            var refreshed=await Live(target,current,token,ct);RequireMatches(current,refreshed);
            if(!current.Entries.Contains(move.Entry))throw new InvalidOperationException("The reviewed meal charge changed before its batch move.");
            var body=MoveBody(refreshed.Single(x=>Int(x,"billingBatchID")==move.Entry.Id),target,move.Entry,destination.BatchId);
            if(!JsonNode.DeepEquals(body,move.Body))throw new InvalidOperationException("The meal charge fields changed before its batch move.");
            using var request=Request(HttpMethod.Put,"/api/billing/batch/student/batch",token);
            request.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");
            using var response=await _http.SendAsync(request,ct);await Read(response,ct);
            var confirmed=false;
            for(var attempt=0;attempt<5;attempt++)
            {
                var after=await _read.ReadAsync(target,destination,ct);
                var expected=move.Entry with{BatchId=destination.BatchId};
                if(after.Entries.Contains(expected))
                {
                    var actual=await Live(target,after,token,ct);RequireMatches(after,actual);
                    if(JsonNode.DeepEquals(body,MoveBody(actual.Single(x=>Int(x,"billingBatchID")==move.Entry.Id),target,expected,destination.BatchId)))
                    {confirmed=true;break;}
                }
                if(attempt<4)await Task.Delay(1000,ct);
            }
            if(!confirmed)throw new InvalidOperationException("The meal-plan batch move is unconfirmed. Refresh and review the existing charge; do not add another charge.");
        }
    }
    internal static JsonObject MoveBody(JsonObject row,MealPlanTaxTarget target,MealPlanRemovalEntry entry,int destination)
    {
        if(destination<=0 || Int(row,"studentUID")!=target.StudentUid || Int(row,"termCalendarID")!=target.TermId ||
            Int(row,"billingBatchID")!=entry.Id || Int(row,"batchMasterID")!=entry.BatchId || Int(row,"transDOCID")!=entry.DocumentId ||
            Int(row,"arTypeID")!=entry.ArTypeId || Amount(row,"amount")!=entry.Amount || Amount(row,"debits")!=entry.Amount || Amount(row,"credits")!=0 ||
            Text(row,"refNo").Trim()!=entry.Reference || Text(row,"description").Trim()!=entry.Description || Text(row,"transType")!="DEBIT")
            throw new InvalidOperationException("The live meal charge differs from the reviewed entry.");
        // These fields cannot safely be discarded by the billing edit endpoint.
        foreach(var field in new[]{"paymentID","creditID","transDistID","creditCardTypeID","typeID"})
            if(!UnsetOrZero(row,field))throw new InvalidOperationException("A payment or distributed meal charge needs manual billing review.");
        if(!UnsetOrZero(row,"paidAmount") || !UnsetOrZero(row,"a1098FilerID") || Text(row,"creditCardNum")!="" || Text(row,"a1098Deductible")!="No")
            throw new InvalidOperationException("A paid or specially configured meal charge needs manual billing review.");
        if(!row.ContainsKey("currencyTypeID") || row["currencyTypeID"] is not null || !UnsetOrZero(row,"currencyTypeAmount"))
            throw new InvalidOperationException("A currency-converted meal charge needs manual billing review.");
        var result=new JsonObject{["batchMasterID"]=destination,["billingBatchID"]=entry.Id,["studentID"]=target.StudentUid,
            ["termID"]=target.TermId,["transDocID"]=entry.DocumentId};
        foreach(var pair in new[]{("allClear","billingAllClear"),("transactionTypeID","transType"),("currencyTypeID","currencyTypeID"),
            ("extendedDocID","extTypeID"),("transactionDate","transDate"),("amount","amount"),("currencyTypeAmt","currencyTypeAmount"),
            ("arTypeID","arTypeID"),("description","description"),("paymentPlanID","paymentPlanHeaderID"),("refNo","refNo")})
        {
            if(!row.ContainsKey(pair.Item2) || pair.Item2=="transDate" && row[pair.Item2] is null)
                throw new InvalidOperationException("Elements omitted a meal billing field; no move was sent.");
            result[pair.Item1]=row[pair.Item2]?.DeepClone();
        }
        return result;
    }
    private async Task<List<JsonObject>> Live(MealPlanTaxTarget target,MealPlanBatchSnapshot snapshot,string token,CancellationToken ct)
    {
        var result=new List<JsonObject>();
        foreach(var batch in snapshot.Batches)
        {
            using var request=Request(HttpMethod.Get,$"/api/billing/cashier-entry/batches/batch-entries-list/{target.StudentUid}/{batch}",token);
            using var response=await _http.SendAsync(request,ct);var data=(await Read(response,ct))["data"] as JsonObject
                ?? throw new InvalidOperationException("Elements returned an invalid shared meal batch.");
            var list=data["batchEntriesList"] is null?new JsonArray():data["batchEntriesList"] as JsonArray
                ?? throw new InvalidOperationException("Elements returned invalid meal batch entries.");
            foreach(var node in list)
            {
                var row=node as JsonObject??throw new InvalidOperationException("Invalid meal charge row.");
                if(Int(row,"studentUID")!=target.StudentUid || Int(row,"batchMasterID")!=batch || Int(row,"billingBatchID")<=0)
                    throw new InvalidOperationException("Elements returned a charge outside the requested student and batch.");
                if(Int(row,"termCalendarID")==target.TermId && (Int(row,"transDOCID")==snapshot.BaseDocument || Int(row,"transDOCID")==_config.GetValue<int>("MealPlanTax:TransactionDocumentId")) && (Amount(row,"debits")!=0 || Amount(row,"credits")!=0))result.Add(row);
            }
        }
        return result;
    }
    private static void RequireMatches(MealPlanBatchSnapshot snapshot,List<JsonObject> live)
    {
        if(live.Count!=snapshot.Entries.Count || live.Select(x=>Int(x,"billingBatchID")).Distinct().Count()!=live.Count ||
            live.Any(row=>!snapshot.Entries.Any(e=>e.Id==Int(row,"billingBatchID") && e.BatchId==Int(row,"batchMasterID") &&
                e.DocumentId==Int(row,"transDOCID") && e.ArTypeId==Int(row,"arTypeID") && e.Amount==Amount(row,"amount") &&
                e.Amount==Amount(row,"debits") && Amount(row,"credits")==0 && e.Reference==Text(row,"refNo").Trim() && e.Description==Text(row,"description").Trim())))
            throw new InvalidOperationException("Live and reporting meal charges disagree. Wait for reporting to refresh before batching.");
    }
    private string Setting(string name)=>_config[MealPlanTaxEnvironment.ApiPath+name]??throw new InvalidOperationException("Meal batch API configuration is incomplete.");
    private async Task<string> Token(CancellationToken ct)
    {
        var key="SharedMealBatch:"+Setting("TenantHost");if(_cache.TryGetValue(key,out string? token)&&!string.IsNullOrWhiteSpace(token))return token;
        using var request=new HttpRequestMessage(HttpMethod.Get,Setting("BaseUrl").TrimEnd('/')+"/api/Login/Authenticate");
        request.Headers.Add("TenantHost",Setting("TenantHost"));request.Headers.Authorization=new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username")+":"+Setting("Password"))));
        using var response=await _http.SendAsync(request,ct);token=(await Read(response,ct))["data"]?["token"]?.GetValue<string>()??throw new InvalidOperationException("Meal batch authentication failed.");
        _cache.Set(key,token,TimeSpan.FromMinutes(10));return token;
    }
    private HttpRequestMessage Request(HttpMethod method,string route,string token)
    {
        MealPlanTaxEnvironment.Validate(_config);var request=new HttpRequestMessage(method,Setting("RegistrationBaseUrl").TrimEnd('/')+route);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Headers.Add("Module","Billing");request.Headers.Add("TenantHost",Setting("TenantHost"));
        request.Headers.Add("Origin","https://"+Setting("TenantHost"));request.Headers.Referrer=new Uri("https://"+Setting("TenantHost"));return request;
    }
    private static int Int(JsonObject row,string name)=>row[name]?.GetValue<int>()??throw new InvalidOperationException("Missing meal charge identity field.");
    private static decimal Amount(JsonObject row,string name)=>decimal.Parse(row[name]?.ToString()??throw new InvalidOperationException("Missing meal charge amount."),CultureInfo.InvariantCulture);
    // Elements uses explicit nulls for unset payment/1098/currency fields on API-created tax rows,
    // and zeroes on Student Life rows. An omitted field still prevents the move.
    private static bool UnsetOrZero(JsonObject row,string name)
    {
        if(!row.ContainsKey(name))throw new InvalidOperationException("Missing meal billing metadata.");
        return row[name] is null || Amount(row,name)==0;
    }
    private static string Text(JsonObject row,string name)=>row[name]?.GetValue<string>()??throw new InvalidOperationException("Missing meal charge text field.");
    private static async Task<JsonObject> Read(HttpResponseMessage response,CancellationToken ct)
    {
        JsonObject? root;try{root=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;}catch(System.Text.Json.JsonException){root=null;}
        if(!response.IsSuccessStatusCode || root?["isSuccess"]?.GetValue<bool>()!=true)throw new InvalidOperationException($"Elements rejected meal batching (HTTP {(int)response.StatusCode}).");return root;
    }
}
