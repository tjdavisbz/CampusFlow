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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsPaymentPostingService :
    IStudentInformationSystemPaymentPostingService, ITransientDependency
{
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string PostingPath = "Payments:Payflow:ElementsPosting";
    private const string TokenCacheKey = "ThesisElements:PaymentPosting:IntegrationToken";
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public ThesisElementsPaymentPostingService(IConfiguration configuration, IMemoryCache cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<StudentPaymentPostingResult> PostAsync(string externalStudentId, int termCalendarId,
        decimal amount, string externalReference, bool isTest, DateTime transactionDate,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));
        if (termCalendarId <= 0) throw new ArgumentOutOfRangeException(nameof(termCalendarId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(externalReference))
            throw new ArgumentException("A gateway reference is required.", nameof(externalReference));

        var token = await GetTokenAsync(cancellationToken);
        var batchComment = isTest ? Posting("TestBatchComment") : Posting("LiveBatchComment");
        var batchId = await FindBatchAsync(token, batchComment, cancellationToken);
        if (batchId is null)
        {
            await CreateBatchAsync(token, batchComment, cancellationToken);
            batchId = await FindBatchAsync(token, batchComment, cancellationToken);
        }

        if (batchId is null)
            throw new InvalidOperationException("Elements created the payment batch but did not return it in the batch list.");

        var existingBillingBatchId = await FindExistingEntryAsync(token, studentUid, batchId.Value,
            externalReference, cancellationToken);
        if (existingBillingBatchId is not null)
            return new StudentPaymentPostingResult(batchId.Value, existingBillingBatchId.Value);

        using var request = CreateRequest(HttpMethod.Post, "/api/billing/batch/student/batch", token);
        request.Content = JsonContent(new
        {
            batchMasterID = batchId.Value,
            billingBatchID = 0,
            allClear = string.Empty,
            studentID = studentUid,
            termID = termCalendarId,
            transactionTypeID = Posting("TransactionType"),
            currencyTypeID = (int?)null,
            extendedDocID = PostingInt("ExtendedDocumentId"),
            transDocID = PostingInt("TransactionDocumentId"),
            transactionDate,
            amount,
            currencyTypeAmt = (decimal?)null,
            arTypeID = PostingInt("ArTypeId"),
            description = Posting("Description"),
            paymentPlanID = 0,
            refNo = externalReference.Trim()
        });
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var root = await ReadRootAsync(response, "payment entry", cancellationToken);
        var billingBatchId = ReadInt(root["data"]);
        if (root["isSuccess"]?.GetValue<bool>() != true || billingBatchId <= 0)
            throw new InvalidOperationException(ReadApiError(root, "Elements did not accept the payment entry."));

        return new StudentPaymentPostingResult(batchId.Value, billingBatchId);
    }

    private async Task<int?> FindBatchAsync(string token, string comment, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/billing/cashier-entry/select-items", token);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var root = await ReadRootAsync(response, "batch lookup", cancellationToken);
        if (root["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(ReadApiError(root, "Elements could not load payment batches."));

        var list = root["data"]?["batchMasterList"] as JsonArray;
        if (list is null) return null;
        foreach (var item in list.OfType<JsonObject>())
        {
            if (!string.Equals(ReadString(item, "comment"), comment, StringComparison.OrdinalIgnoreCase)) continue;
            var id = ReadInt(item["batchMasterID"]);
            if (id > 0) return id;
        }
        return null;
    }

    private async Task CreateBatchAsync(string token, string comment, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post,
            "/api/billing/cashier-entry/batches/batch-master", token);
        request.Content = JsonContent(new
        {
            campus = PostingInt("CampusId"),
            comment,
            mod = PostingInt("SourceModuleId"),
            termBased = true
        });
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var root = await ReadRootAsync(response, "batch creation", cancellationToken);
        if (root["isSuccess"]?.GetValue<bool>() != true || root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(ReadApiError(root, "Elements did not create the payment batch."));
    }

    private async Task<int?> FindExistingEntryAsync(string token, int studentUid, int batchMasterId,
        string reference, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"/api/billing/cashier-entry/batches/batch-entries-list/{studentUid}/{batchMasterId}", token);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var root = await ReadRootAsync(response, "batch entry lookup", cancellationToken);
        if (root["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(ReadApiError(root, "Elements could not check the payment batch."));

        var list = root["data"]?["batchEntriesList"] as JsonArray;
        if (list is null) return null;
        foreach (var item in list.OfType<JsonObject>())
        {
            if (!string.Equals(ReadString(item, "refNo"), reference.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            var id = ReadInt(item["billingBatchID"]);
            if (id > 0) return id;
        }
        return null;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Api("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Api("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Api("Username")}:{Api("Password")}")));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var root = await ReadRootAsync(response, "authentication", cancellationToken);
        var token = root["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements authentication returned no token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route, string token)
    {
        var tenantHost = Api("TenantHost").TrimEnd('/');
        var request = new HttpRequestMessage(method,
            $"{Api("RegistrationBaseUrl").TrimEnd('/')}/{route.TrimStart('/')}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "Billing");
        request.Headers.Add("TenantHost", tenantHost);
        request.Headers.Add("Origin", $"https://{tenantHost}");
        request.Headers.Referrer = new Uri($"https://{tenantHost}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static StringContent JsonContent(object value) => new(
        JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonObject> ReadRootAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Elements {operation} failed ({(int)response.StatusCode}).");
        return JsonNode.Parse(body) as JsonObject
               ?? throw new InvalidOperationException($"Elements {operation} returned an invalid response.");
    }

    private static string? ReadString(JsonObject item, string name) =>
        item[name]?.GetValue<string>()?.Trim();

    private static int ReadInt(JsonNode? node)
    {
        if (node is null) return 0;
        return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : 0;
    }

    private static string ReadApiError(JsonObject root, string fallback) =>
        root["message"]?.GetValue<string>() is { Length: > 0 } message ? message : fallback;

    private string Api(string name) => _configuration[$"{ApiPath}:{name}"]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private string Posting(string name) => _configuration[$"{PostingPath}:{name}"]
        ?? throw new InvalidOperationException($"Missing Elements payment-posting setting '{name}'.");

    private int PostingInt(string name) => int.TryParse(Posting(name), out var value)
        ? value : throw new InvalidOperationException($"Elements payment-posting setting '{name}' is invalid.");
}
