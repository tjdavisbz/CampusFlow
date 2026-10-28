using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

// All money in this batch is calculated by CampusFlow. Elements' housing rent remains $0.
// This adapter is deliberately locked to Nelson's non-production Elements tenant.
public sealed class ElementsHousingBillingWriter : IElementsHousingBillingWriter, ITransientDependency
{
    private const string BatchComment = "CampusFlow Housing Charges (NON-PRODUCTION)";
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private const string TokenCacheKey = "ThesisElements:NonProductionHousing:BillingToken";
    private static readonly HttpClient SharedClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IElementsHousingAssignmentWriter _assignmentWriter;
    private readonly HttpClient _client;

    public ElementsHousingBillingWriter(IConfiguration configuration, IMemoryCache cache,
        IElementsHousingAssignmentWriter assignmentWriter)
        : this(configuration, cache, assignmentWriter, SharedClient) { }

    internal ElementsHousingBillingWriter(IConfiguration configuration, IMemoryCache cache,
        IElementsHousingAssignmentWriter assignmentWriter, HttpClient client)
    {
        _configuration = configuration;
        _cache = cache;
        _assignmentWriter = assignmentWriter;
        _client = client;
    }

    public async Task VerifyCanReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyCollection<int> housingDocumentIds, CancellationToken cancellationToken = default)
    {
        _assignmentWriter.EnsureNonProductionWriteEnabled();
        if (studentUid <= 0 || termCalendarId != 268 || housingDocumentIds.Count == 0 ||
            housingDocumentIds.Any(x => x <= 0))
            throw new InvalidOperationException("The housing billing scope is invalid.");
        var documents = housingDocumentIds.Distinct().Order().ToArray();
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(cancellationToken);
        var parameterNames = documents.Select((_, index) => "@Doc" + index).ToArray();
        await using var command = new SqlCommand($"""
            SELECT (SELECT COUNT(*) FROM dbo.Billing b
                    WHERE b.OwnerUID = @StudentUid AND b.TermCalendarID = @TermId
                      AND b.TransDOCID IN ({string.Join(",", parameterNames)})
                      AND (b.Debits <> 0 OR b.Credits <> 0))
                 + (SELECT COUNT(*) FROM dbo.BillingBatch b
                    LEFT JOIN dbo.BatchMaster master ON master.BatchMasterID = b.BatchMasterID
                    WHERE b.OwnerUID = @StudentUid AND b.TermCalendarID = @TermId
                      AND b.TransDOCID IN ({string.Join(",", parameterNames)})
                      AND (b.Debits <> 0 OR b.Credits <> 0)
                      AND (master.Comment IS NULL OR master.Comment <> @BatchComment));
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@StudentUid", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@TermId", SqlDbType.Int).Value = termCalendarId;
        command.Parameters.Add("@BatchComment", SqlDbType.VarChar, 100).Value = BatchComment;
        for (var i = 0; i < documents.Length; i++)
            command.Parameters.Add(parameterNames[i], SqlDbType.Int).Value = documents[i];
        if ((int)(await command.ExecuteScalarAsync(cancellationToken) ?? -1) != 0)
            throw new InvalidOperationException("This student already has a non-CampusFlow housing charge for the term. Review billing before changing it; CampusFlow will not duplicate or delete that charge.");
    }

    public async Task ReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        CancellationToken cancellationToken = default)
    {
        await VerifyCanReconcileAsync(studentUid, termCalendarId, housingDocumentIds, cancellationToken);
        if (desired.Count > 2 || desired.Any(x => x.Amount <= 0 || x.TransactionDocumentId <= 0))
            throw new InvalidOperationException("The proposed housing charge lines are invalid.");
        var token = await GetTokenAsync(cancellationToken);
        var batchId = await FindBatchAsync(token, cancellationToken);
        if (batchId is null && desired.Count == 0) return;
        if (batchId is null)
        {
            await CreateBatchAsync(token, cancellationToken);
            batchId = await FindBatchAsync(token, cancellationToken)
                ?? throw new InvalidOperationException("Elements created a housing batch but did not return it in the batch list.");
        }
        var existing = await GetEntriesAsync(token, studentUid, batchId.Value, cancellationToken);
        var references = new[] { Reference(termCalendarId, studentUid, "BASE"),
            Reference(termCalendarId, studentUid, "SINGLE") };
        if (existing.Any(x => x.Reference.StartsWith("CFH-", StringComparison.OrdinalIgnoreCase) &&
                              !references.Contains(x.Reference, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An unrecognized CampusFlow housing charge was found in the batch.");
        for (var i = 0; i < references.Length; i++)
        {
            var matches = existing.Where(x => string.Equals(x.Reference, references[i], StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException("Duplicate CampusFlow housing charge references require manual review.");
            var prior = matches.SingleOrDefault();
            var wanted = i < desired.Count ? desired[i] : null;
            if (prior is null && wanted is null) continue;
            if (wanted is null)
            {
                await DeleteEntryAsync(token, prior!.Id, cancellationToken);
            }
            else if (prior is null)
            {
                await SaveEntryAsync(token, HttpMethod.Post, batchId.Value, 0, studentUid,
                    termCalendarId, references[i], wanted, cancellationToken);
            }
            else if (prior.TransactionDocumentId != wanted.TransactionDocumentId || prior.Amount != wanted.Amount)
            {
                await SaveEntryAsync(token, HttpMethod.Put, batchId.Value, prior.Id, studentUid,
                    termCalendarId, references[i], wanted, cancellationToken);
            }
        }
        var confirmed = await GetEntriesAsync(token, studentUid, batchId.Value, cancellationToken);
        for (var i = 0; i < references.Length; i++)
        {
            var matches = confirmed.Where(x => string.Equals(x.Reference, references[i], StringComparison.OrdinalIgnoreCase)).ToList();
            if (i >= desired.Count && matches.Count == 0) continue;
            if (i < desired.Count && matches.Count == 1 &&
                matches[0].Amount == desired[i].Amount &&
                matches[0].TransactionDocumentId == desired[i].TransactionDocumentId) continue;
            throw new InvalidOperationException("Elements did not confirm the expected CampusFlow housing charges.");
        }
    }

    private async Task<int?> FindBatchAsync(string token, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "/api/billing/cashier-entry/select-items", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"]?["batchMasterList"] is not JsonArray batches)
            throw new InvalidOperationException("Elements returned an invalid housing batch list.");
        var matches = batches.OfType<JsonObject>()
            .Where(x => string.Equals(x["comment"]?.GetValue<string>(), BatchComment, StringComparison.OrdinalIgnoreCase))
            .Select(x => x["batchMasterID"]?.GetValue<int>() ?? 0).Where(x => x > 0).Distinct().ToList();
        if (matches.Count > 1) throw new InvalidOperationException("Multiple CampusFlow housing batches require manual review.");
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task CreateBatchAsync(string token, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "/api/billing/cashier-entry/batches/batch-master", token);
        request.Content = JsonContent(new { campus = 0, comment = BatchComment, mod = 2, termBased = true });
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not create the CampusFlow housing batch.");
    }

    private async Task<List<BatchEntry>> GetEntriesAsync(string token, int studentUid, int batchId,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get,
            $"/api/billing/cashier-entry/batches/batch-entries-list/{studentUid}/{batchId}", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"] is not JsonObject)
            throw new InvalidOperationException("Elements returned an invalid housing charge list.");
        if (root["data"]?["batchEntriesList"] is null) return [];
        if (root["data"]?["batchEntriesList"] is not JsonArray entries)
            throw new InvalidOperationException("Elements returned an invalid housing charge list.");
        return entries.OfType<JsonObject>().Select(x => new BatchEntry(
            x["billingBatchID"]?.GetValue<int>() ?? 0,
            x["refNo"]?.GetValue<string>()?.Trim() ?? "",
            x["transDOCID"]?.GetValue<int>() ?? 0,
            decimal.Parse(x["amount"]?.ToString() ?? "0", CultureInfo.InvariantCulture)))
            .ToList();
    }

    private async Task SaveEntryAsync(string token, HttpMethod method, int batchId, int entryId,
        int studentUid, int termCalendarId, string reference, HousingChargeLine line,
        CancellationToken cancellationToken)
    {
        using var request = Request(method, "/api/billing/batch/student/batch", token);
        request.Content = JsonContent(new
        {
            batchMasterID = batchId, billingBatchID = entryId, allClear = "",
            studentID = studentUid, termID = termCalendarId,
            transactionTypeID = "DEBIT", currencyTypeID = (int?)null,
            extendedDocID = 0, transDocID = line.TransactionDocumentId,
            transactionDate = DateTime.Now.Date, amount = line.Amount,
            currencyTypeAmt = (decimal?)null, arTypeID = line.AccountsReceivableTypeId,
            description = line.Description, paymentPlanID = 0, refNo = reference
        });
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (method == HttpMethod.Post && (root["data"]?.GetValue<int>() ?? 0) <= 0)
            throw new InvalidOperationException("Elements did not create the CampusFlow housing charge.");
        if (method == HttpMethod.Put && root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not update the CampusFlow housing charge.");
    }

    private async Task DeleteEntryAsync(string token, int entryId, CancellationToken cancellationToken)
    {
        if (entryId <= 0) throw new InvalidOperationException("The CampusFlow charge entry ID is invalid.");
        using var request = Request(HttpMethod.Delete,
            $"/api/billing/cashier-entry/batches/batch-entry/{entryId}", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not delete the CampusFlow housing charge.");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        _assignmentWriter.EnsureNonProductionWriteEnabled();
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Non-production Elements billing authentication failed.");
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var token = root?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Non-production Elements did not return a billing token.");
        _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage Request(HttpMethod method, string route, string token)
    {
        _assignmentWriter.EnsureNonProductionWriteEnabled();
        var tenantHost = Setting("TenantHost");
        var request = new HttpRequestMessage(method, $"{Setting("RegistrationBaseUrl").TrimEnd('/')}{route}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "Billing");
        request.Headers.Add("TenantHost", tenantHost);
        request.Headers.Add("Origin", $"https://{tenantHost}");
        request.Headers.Referrer = new Uri($"https://{tenantHost}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? root;
        try { root = JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { root = null; }
        if (!response.IsSuccessStatusCode || root?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"Non-production Elements rejected the housing billing request (HTTP {(int)response.StatusCode}).");
        return root;
    }

    private string Setting(string name) => _configuration[ApiPath + name]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private static string Reference(int termCalendarId, int studentUid, string component) =>
        $"CFH-{termCalendarId}-{studentUid}-{component}";

    private sealed record BatchEntry(int Id, string Reference, int TransactionDocumentId, decimal Amount);
}
