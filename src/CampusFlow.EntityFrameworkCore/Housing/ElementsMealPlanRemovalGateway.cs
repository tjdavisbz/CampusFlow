using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CampusFlow.MealPlanTaxes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Housing;

public sealed partial class ElementsMealPlanRemovalGateway : IMealPlanRemovalGateway, ITransientDependency
{
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly IElementsMealPlanTaxReadModel _tax;
    private readonly HttpClient _http;
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(60) };
    public ElementsMealPlanRemovalGateway(IConfiguration config, IMemoryCache cache, IElementsMealPlanTaxReadModel tax)
        : this(config, cache, tax, Shared) { }
    internal ElementsMealPlanRemovalGateway(IConfiguration config, IMemoryCache cache, IElementsMealPlanTaxReadModel tax, HttpClient http)
    { _config = config; _cache = cache; _tax = tax; _http = http; }
    private void Guard(MealPlanRemovalTarget target)
    {
        MealPlanTaxEnvironment.Validate(_config);
        if (!_config.GetValue<bool>("MealPlans:EnableAdministrativeRemoval") || target.StudentUid <= 0 || target.TermId <= 0 || target.AssignmentId <= 0 || target.MealPlanId <= 0 || target.Amount < 0)
            throw new InvalidOperationException("Review a valid meal assignment in an enabled removal environment.");
    }
    public async Task<List<MealPlanRemovalEntry>> ReviewAsync(MealPlanRemovalTarget target)
    {
        Guard(target);
        var reported = await Reported(target);
        var token = await Token(); var live = new List<MealPlanRemovalEntry>();
        foreach (var batch in reported.Batches)
            live.AddRange((await Live(target.StudentUid, batch, token)).Where(x => x.Term == target.TermId &&
                (x.Entry.DocumentId == reported.BaseDocument || x.Entry.DocumentId == reported.Tax.DocumentId))
                .Select(x => x.Entry with { Kind = x.Entry.DocumentId == reported.BaseDocument ? "meal plan" : "sales tax" }));
        if (live.Count != reported.Entries.Count || live.Any(x => !reported.Entries.Contains(x)))
            throw new InvalidOperationException("The live billing batch and reporting entries disagree. Wait for Elements reporting to refresh before reviewing removal.");
        return reported.Entries.OrderBy(x => x.Id).ToList();
    }
    public async Task DeleteAsync(MealPlanRemovalTarget target, MealPlanRemovalEntry entry)
    {
        var current = await ReviewAsync(target);
        if (!current.Any(x => x.Id == entry.Id)) return;
        if (!current.Contains(entry)) throw new InvalidOperationException("The reviewed charge changed; no deletion was sent.");
        var token = await Token();
        using var request = Request(HttpMethod.Delete, $"/api/billing/cashier-entry/batches/batch-entry/{entry.Id}", token);
        using var response = await _http.SendAsync(request);
        if ((await Read(response))["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not confirm removal of the reviewed charge.");
        for (var attempt = 0; attempt < 6; attempt++)
        {
            // Reported also checks the ledger: disappearing from a batch must not be
            // mistaken for deletion when another process has posted the entry.
            var reported = await Reported(target);
            var live = await Live(target.StudentUid, entry.BatchId, token);
            if (!reported.Entries.Any(x => x.Id == entry.Id) && !live.Any(x => x.Entry.Id == entry.Id)) return;
            if (attempt < 5) await Task.Delay(1000);
        }
        throw new InvalidOperationException("Charge removal is not yet confirmed in both live and reporting data.");
    }
    private string Setting(string name) => _config[MealPlanTaxEnvironment.ApiPath + name] ?? throw new InvalidOperationException("Meal removal API configuration is incomplete.");
    private async Task<string> Token()
    {
        var key = "MealRemoval:" + Setting("TenantHost");
        if (_cache.TryGetValue(key, out string? token) && !string.IsNullOrEmpty(token)) return token;
        using var request = new HttpRequestMessage(HttpMethod.Get, Setting("BaseUrl").TrimEnd('/') + "/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username") + ":" + Setting("Password"))));
        using var response = await _http.SendAsync(request);
        token = (await Read(response))["data"]?["token"]?.GetValue<string>() ?? throw new InvalidOperationException("Meal removal authentication failed.");
        _cache.Set(key, token, TimeSpan.FromMinutes(10)); return token;
    }
    private HttpRequestMessage Request(HttpMethod method, string route, string token)
    {
        MealPlanTaxEnvironment.Validate(_config);
        var request = new HttpRequestMessage(method, Setting("RegistrationBaseUrl").TrimEnd('/') + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("TenantHost", Setting("TenantHost")); request.Headers.Add("Module", "Billing");
        request.Headers.Add("Origin", "https://" + Setting("TenantHost")); request.Headers.Referrer = new("https://" + Setting("TenantHost")); return request;
    }
    private sealed record LiveCharge(MealPlanRemovalEntry Entry, int Term);
    private async Task<List<LiveCharge>> Live(int student, int batch, string token)
    {
        using var request = Request(HttpMethod.Get, $"/api/billing/cashier-entry/batches/batch-entries-list/{student}/{batch}", token);
        using var response = await _http.SendAsync(request); var data = (await Read(response))["data"] as JsonObject
            ?? throw new InvalidOperationException("Invalid meal removal batch response.");
        var rows = data["batchEntriesList"] is null ? new JsonArray() : data["batchEntriesList"] as JsonArray
            ?? throw new InvalidOperationException("Invalid meal removal entries.");
        var result = new List<LiveCharge>();
        foreach (var node in rows)
        {
            var row = node as JsonObject ?? throw new InvalidOperationException("Invalid charge row.");
            int Int(string name) => row[name]?.GetValue<int>() ?? 0;
            if (Int("studentUID") != student || Int("batchMasterID") != batch || Int("billingBatchID") <= 0)
                throw new InvalidOperationException("The live charge belongs to a different student or batch.");
            result.Add(new(new(Int("billingBatchID"), batch, Int("transDOCID"), Int("arTypeID"),
                decimal.Parse(row["amount"]?.ToString() ?? throw new InvalidOperationException("Missing charge amount."), CultureInfo.InvariantCulture),
                row["refNo"]?.GetValue<string>()?.Trim() ?? "", row["description"]?.GetValue<string>()?.Trim() ?? "", ""), Int("termCalendarID")));
        }
        if (result.Select(x => x.Entry.Id).Distinct().Count() != result.Count) throw new InvalidOperationException("Duplicate live billing entries.");
        return result;
    }
    private static async Task<JsonObject> Read(HttpResponseMessage response)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject; }
        catch (System.Text.Json.JsonException) { root = null; }
        if (!response.IsSuccessStatusCode || root?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"Elements rejected the meal removal request (HTTP {(int)response.StatusCode}).");
        return root;
    }
}
