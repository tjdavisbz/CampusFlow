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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

// CampusFlow replaces only the affected student's unposted housing batch entries.
// Existing room-rate setup and posted ledger transactions are never changed here.
public sealed partial class ElementsHousingBillingWriter : IElementsHousingBillingWriter, ITransientDependency
{
    private const string BatchComment = "Housing Charges";
    private const string ElementsRentBatchComment = "Housing charges Rent and Security Deposit";
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly HttpClient SharedClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IElementsHousingAssignmentWriter _assignmentWriter;
    private readonly HttpClient _client;
    private readonly Func<int,int,int,CancellationToken,Task<List<BatchEntry>>> _legacyEntries;

    public ElementsHousingBillingWriter(IConfiguration configuration, IMemoryCache cache,
        IElementsHousingAssignmentWriter assignmentWriter)
        : this(configuration, cache, assignmentWriter, SharedClient) { }

    internal ElementsHousingBillingWriter(IConfiguration configuration, IMemoryCache cache,
        IElementsHousingAssignmentWriter assignmentWriter, HttpClient client,
        Func<int,int,int,CancellationToken,Task<List<BatchEntry>>>? legacyEntries = null)
    {
        _configuration = configuration;
        _cache = cache;
        _assignmentWriter = assignmentWriter;
        _client = client;
        _legacyEntries = legacyEntries ?? ReadLegacyEntriesAsync;
    }

    public async Task VerifyCanReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyCollection<int> housingDocumentIds, CancellationToken cancellationToken = default)
    {
        _assignmentWriter.EnsureWriteEnabled();
        if (studentUid <= 0 || termCalendarId is not (268 or 269) || housingDocumentIds.Count == 0 ||
            housingDocumentIds.Any(x => x <= 0))
            throw new InvalidOperationException("The housing billing scope is invalid.");
        var batchId = await ResolveBatchAsync(cancellationToken);
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
                    LEFT JOIN dbo.BatchMaster m ON m.BatchMasterID = b.BatchMasterID
                    WHERE b.OwnerUID = @StudentUid AND b.TermCalendarID = @TermId
                      AND b.TransDOCID IN ({string.Join(",", parameterNames)})
                      AND (b.Debits <> 0 OR b.Credits <> 0)
                      AND b.BatchMasterID <> @BatchMasterId
                      AND NOT (m.Comment = @ElementsRentBatchComment
                               AND COALESCE(b.Credits, 0) = 0
                               AND NULLIF(LTRIM(RTRIM(b.RefNo)), '') IS NULL
                               AND b.Description = 'Rent Per Person' AND b.ARTypeID = 0))
                 + (SELECT COUNT(*) FROM dbo.BillingBatch b
                    WHERE b.OwnerUID = @StudentUid AND b.TermCalendarID = @TermId
                      AND b.TransDOCID IN ({string.Join(",", parameterNames)})
                      AND b.BatchMasterID = @BatchMasterId AND COALESCE(b.Credits, 0) <> 0);
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@StudentUid", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@TermId", SqlDbType.Int).Value = termCalendarId;
        command.Parameters.Add("@BatchMasterId", SqlDbType.Int).Value = batchId;
        command.Parameters.Add("@ElementsRentBatchComment", SqlDbType.VarChar, 100).Value = ElementsRentBatchComment;
        for (var i = 0; i < documents.Length; i++)
            command.Parameters.Add(parameterNames[i], SqlDbType.Int).Value = documents[i];
        if ((int)(await command.ExecuteScalarAsync(cancellationToken) ?? -1) != 0)
            throw new InvalidOperationException("This student already has a non-CampusFlow housing charge for the term. Review billing before changing it; CampusFlow will not duplicate or delete that charge.");
        var token = await GetTokenAsync(cancellationToken);
        await ReadOwnedEntriesAsync(token, studentUid, termCalendarId, batchId, housingDocumentIds, cancellationToken);
    }

    public Task ReconcileAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        CancellationToken cancellationToken = default) =>
        ReconcileCoreAsync(studentUid, termCalendarId, desired, housingDocumentIds, null, cancellationToken);

    public Task ReconcileReviewedAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        string reviewRevision, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewRevision)) throw new InvalidOperationException("A current billing review is required.");
        return ReconcileCoreAsync(studentUid, termCalendarId, desired, housingDocumentIds, reviewRevision, cancellationToken);
    }

    public async Task<HousingBillingReview> ReviewAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        CancellationToken cancellationToken = default)
    {
        await VerifyCanReconcileAsync(studentUid, termCalendarId, housingDocumentIds, cancellationToken);
        ValidateDesired(desired);
        var token = await GetTokenAsync(cancellationToken);
        var batchId = await ResolveBatchAsync(cancellationToken);
        var owned = await ReadOwnedEntriesAsync(token, studentUid, termCalendarId, batchId, housingDocumentIds, cancellationToken);
        return BuildReview(studentUid, termCalendarId, desired, owned.Entries, owned.Legacy,
            await ReadGeneratedRentAsync(token, studentUid, termCalendarId, housingDocumentIds, cancellationToken));
    }

    private static void ValidateDesired(IReadOnlyList<HousingChargeLine> desired)
    {
        if (desired.Count > 2 || desired.Any(x => x.Amount <= 0 || x.TransactionDocumentId <= 0))
            throw new InvalidOperationException("The proposed housing charge lines are invalid.");
    }

    private async Task ReconcileCoreAsync(int studentUid, int termCalendarId,
        IReadOnlyList<HousingChargeLine> desired, IReadOnlyCollection<int> housingDocumentIds,
        string? reviewRevision, CancellationToken cancellationToken)
    {
        await VerifyCanReconcileAsync(studentUid, termCalendarId, housingDocumentIds, cancellationToken);
        ValidateDesired(desired);
        var token = await GetTokenAsync(cancellationToken);
        var batchId = await ResolveBatchAsync(cancellationToken);
        var owned = await ReadOwnedEntriesAsync(token, studentUid, termCalendarId, batchId, housingDocumentIds, cancellationToken);
        if (reviewRevision is not null && BuildReview(studentUid, termCalendarId, desired, owned.Entries, owned.Legacy,
                await ReadGeneratedRentAsync(token, studentUid, termCalendarId, housingDocumentIds, cancellationToken)).Revision != reviewRevision)
            throw new InvalidOperationException("The reviewed housing charges changed. Review billing again before resuming.");
        var references = new[] { Reference(termCalendarId, studentUid, "BASE"), Reference(termCalendarId, studentUid, "SINGLE") };
        var replaceable = owned.Entries;
        for (var i = 0; i < references.Length; i++)
        {
            var matches = replaceable.Where(x => string.Equals(x.Reference, references[i], StringComparison.OrdinalIgnoreCase) || i == 0 && owned.Legacy.Contains(x)).ToList();
            var prior = matches.SingleOrDefault();
            var wanted = i < desired.Count ? desired[i] : null;
            if (prior is null && wanted is null) continue;
            if (wanted is null)
            {
                await DeleteEntryAsync(token, prior!.Id, cancellationToken);
            }
            else if (prior is null)
            {
                await SaveEntryAsync(token, HttpMethod.Post, batchId, 0, studentUid,
                    termCalendarId, references[i], wanted, cancellationToken);
            }
            else if (owned.Legacy.Contains(prior) || prior.TransactionDocumentId != wanted.TransactionDocumentId || prior.Amount != wanted.Amount || prior.AccountsReceivableTypeId != wanted.AccountsReceivableTypeId)
            {
                await SaveEntryAsync(token, HttpMethod.Put, batchId, prior.Id, studentUid,
                    termCalendarId, references[i], wanted, cancellationToken);
            }
        }
        var confirmed = await GetEntriesAsync(token, studentUid, batchId, cancellationToken);
        if (confirmed.Any(x => x.TermCalendarId == termCalendarId &&
                               housingDocumentIds.Contains(x.TransactionDocumentId) &&
                               !references.Contains(x.Reference, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Elements still has an unexpected housing charge for this student and term.");
        for (var i = 0; i < references.Length; i++)
        {
            var matches = confirmed.Where(x => string.Equals(x.Reference, references[i], StringComparison.OrdinalIgnoreCase)).ToList();
            if (i >= desired.Count && matches.Count == 0) continue;
            if (i < desired.Count && matches.Count == 1 &&
                matches[0].Amount == desired[i].Amount &&
                matches[0].TransactionDocumentId == desired[i].TransactionDocumentId &&
                matches[0].AccountsReceivableTypeId == desired[i].AccountsReceivableTypeId) continue;
            throw new InvalidOperationException("Elements did not confirm the expected CampusFlow housing charges.");
        }
        await RemoveElementsGeneratedRentAsync(token, studentUid, termCalendarId,
            housingDocumentIds, cancellationToken);
    }

    internal static void RequireOwnedReferences(IEnumerable<string> references,IReadOnlyCollection<string> expected)
    {
        if(references.Any(x=>!expected.Contains(x,StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An unrecognized housing charge is in the CampusFlow batch. Review it manually; it will not be replaced or deleted.");
    }

    private async Task<int> ResolveBatchAsync(CancellationToken ct)
    {
        var configured=ConfiguredBatchId();
        await using var connection=new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(ct);
        await using var query=new SqlCommand("""
            SELECT BatchMasterID,Comment,Status,SourceModuleID,TermBased FROM dbo.BatchMaster
            WHERE BatchMasterID=@Configured OR (Comment=@Comment AND Status='Open' AND SourceModuleID=2 AND TermBased=1);
            """,connection);
        query.Parameters.AddWithValue("@Configured",configured);query.Parameters.AddWithValue("@Comment",BatchComment);
        var batches=new List<SharedBillingBatchIdentity>();await using var rows=await query.ExecuteReaderAsync(ct);
        while(await rows.ReadAsync(ct))batches.Add(new(rows.GetInt32(0),rows.IsDBNull(1)?"":rows.GetString(1),rows.IsDBNull(2)?"":rows.GetString(2),rows.IsDBNull(3)?0:rows.GetInt32(3),!rows.IsDBNull(4)&&Convert.ToBoolean(rows.GetValue(4))));
        return SharedBillingBatchRules.Resolve(configured,BatchComment,batches);
    }

    private async Task<IReadOnlyList<int>> FindBatchesByCommentAsync(string token, string comment,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "/api/billing/cashier-entry/select-items", token);
        using var response = await ElementsHousingAuthentication.SendAsync(_configuration,_cache,_client,request,cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"]?["batchMasterList"] is not JsonArray batches)
            throw new InvalidOperationException("Elements returned an invalid housing batch list.");
        var matches = batches.OfType<JsonObject>()
            .Where(x => string.Equals(x["comment"]?.GetValue<string>(), comment, StringComparison.OrdinalIgnoreCase))
            .Select(x => x["batchMasterID"]?.GetValue<int>() ?? 0).Where(x => x > 0).Distinct().ToList();
        // Elements can have several generated-rent batches. Read each batch but mutate
        // only the verified generated entry for this one student and term.
        // The cashier selector can omit an open batch that is still accessible by ID.
        // The replica is used only to identify that batch; its entries are read live above.
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT BatchMasterID FROM dbo.BatchMaster
            WHERE Comment = @Comment AND Status = 'Open' AND SourceModuleID IN (2,15);
            """, connection);
        command.Parameters.Add("@Comment", SqlDbType.VarChar, 100).Value = comment;
        var replicaMatches = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) replicaMatches.Add(reader.GetInt32(0));
        return matches.Concat(replicaMatches).Distinct().Order().ToArray();
    }

    private async Task<List<BatchEntry>> ReadGeneratedRentAsync(string token, int studentUid, int termCalendarId,
        IReadOnlyCollection<int> housingDocumentIds, CancellationToken cancellationToken)
    {
        var result = new List<BatchEntry>();
        foreach (var batchId in await FindBatchesByCommentAsync(token, ElementsRentBatchComment, cancellationToken))
        {
            var entries = await GetEntriesAsync(token, studentUid, batchId, cancellationToken);
            result.AddRange(entries.Where(x => x.StudentUid == studentUid && x.TermCalendarId == termCalendarId &&
                housingDocumentIds.Contains(x.TransactionDocumentId) && x.Amount >= 0 &&
                x.AccountsReceivableTypeId == 0 && string.IsNullOrEmpty(x.Reference) &&
                string.Equals(x.Description, "Rent Per Person", StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }

    private async Task RemoveElementsGeneratedRentAsync(string token, int studentUid, int termCalendarId,
        IReadOnlyCollection<int> housingDocumentIds, CancellationToken cancellationToken)
    {
        var generated = await ReadGeneratedRentAsync(token, studentUid, termCalendarId, housingDocumentIds, cancellationToken);
        foreach (var entry in generated) await DeleteEntryAsync(token, entry.Id, cancellationToken);
        foreach (var batch in generated.GroupBy(x => x.BatchMasterId))
        {
            var remaining = await GetEntriesAsync(token, studentUid, batch.Key, cancellationToken);
            if (batch.Any(x => remaining.Any(y => y.Id == x.Id)))
                throw new InvalidOperationException("Elements did not confirm removal of its generated rent entry.");
        }
    }

    private async Task<List<BatchEntry>> GetEntriesAsync(string token, int studentUid, int batchId,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get,
            $"/api/billing/cashier-entry/batches/batch-entries-list/{studentUid}/{batchId}", token);
        using var response = await ElementsHousingAuthentication.SendAsync(_configuration,_cache,_client,request,cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"] is not JsonObject)
            throw new InvalidOperationException("Elements returned an invalid housing charge list.");
        if (root["data"]?["batchEntriesList"] is null) return [];
        if (root["data"]?["batchEntriesList"] is not JsonArray entries)
            throw new InvalidOperationException("Elements returned an invalid housing charge list.");
        if (entries.Any(x => x is not JsonObject)) throw new InvalidOperationException("Elements returned an invalid housing charge row.");
        var result = entries.OfType<JsonObject>().Select(x => new BatchEntry(
            x["billingBatchID"]?.GetValue<int>() ?? 0,
            x["batchMasterID"]?.GetValue<int>() ?? 0,
            x["refNo"]?.GetValue<string>()?.Trim() ?? "",
            x["transDOCID"]?.GetValue<int>() ?? 0,
            decimal.Parse(x["amount"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
            x["studentUID"]?.GetValue<int>() ?? 0,
            x["termCalendarID"]?.GetValue<int>() ?? 0,
            x["arTypeID"]?.GetValue<int>() ?? 0,
            x["description"]?.GetValue<string>()?.Trim() ?? ""))
            .ToList();
        if (result.Any(x => x.Id <= 0 || x.BatchMasterId != batchId || x.StudentUid != studentUid))
            throw new InvalidOperationException("Elements returned a housing charge outside the requested student or batch.");
        return result;
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
        using var response = await ElementsHousingAuthentication.SendAsync(_configuration,_cache,_client,request,cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (method == HttpMethod.Post && (root["data"]?.GetValue<int>() ?? 0) <= 0)
            throw new InvalidOperationException("Elements did not create the CampusFlow housing charge.");
        if (method == HttpMethod.Put && root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not update the CampusFlow housing charge.");
    }

    private async Task DeleteEntryAsync(string token, int entryId, CancellationToken cancellationToken)
    {
        if (entryId <= 0) throw new InvalidOperationException("The housing batch entry ID is invalid.");
        using var request = Request(HttpMethod.Delete,
            $"/api/billing/cashier-entry/batches/batch-entry/{entryId}", token);
        using var response = await ElementsHousingAuthentication.SendAsync(_configuration,_cache,_client,request,cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        if (root["data"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not delete the housing batch entry.");
    }

    private Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        _assignmentWriter.EnsureWriteEnabled();
        return ElementsHousingAuthentication.GetAsync(_configuration,_cache,_client,cancellationToken);
    }

    private HttpRequestMessage Request(HttpMethod method, string route, string token)
    {
        _assignmentWriter.EnsureWriteEnabled();
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
            throw new InvalidOperationException($"Elements rejected the housing billing request (HTTP {(int)response.StatusCode}).");
        return root;
    }

    private string Setting(string name) => _configuration[ApiPath + name]
        ?? throw new InvalidOperationException($"Missing Elements API setting '{name}'.");

    private int ConfiguredBatchId()
    {
        if (!int.TryParse(_configuration["Housing:ElementsBatchMasterId"], out var id) || id <= 0)
            throw new InvalidOperationException("The CampusFlow housing batch ID is not configured.");
        return id;
    }

    private static string Reference(int termCalendarId, int studentUid, string component) =>
        $"CFH-{termCalendarId}-{studentUid}-{component}";

    internal sealed record BatchEntry(int Id, int BatchMasterId, string Reference, int TransactionDocumentId, decimal Amount,
        int StudentUid, int TermCalendarId, int AccountsReceivableTypeId, string Description);
}
