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
using CampusFlow.BillApprovals;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ElementsPaymentPlanFeeService(IConfiguration configuration,
    IElementsHousingAssignmentWriter environmentGuard) : IPaymentPlanFeeService, ITransientDependency
{
    public const string BatchComment = "Payment Plan Fees";
    private const string Api = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private string Setting(string name) => configuration[Api + name] ?? throw new InvalidOperationException("Elements billing is not configured.");
    private async Task<SqlConnection> Open(CancellationToken ct)
    { environmentGuard.EnsureWriteEnabled(); var c = new SqlConnection(configuration.GetConnectionString("ThesisElementsReadOnly")); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }

    private async Task<(int Doc, int Ar)> Definition(decimal fee, CancellationToken ct)
    {
        if (fee <= 0) throw new ArgumentOutOfRangeException(nameof(fee));
        await using var c = await Open(ct);
        await using var q = new SqlCommand("SELECT TransDocID,ARTypeID,DefaultAmount,TransType,ReportFlag,ActiveFlag FROM dbo.Transdoc WHERE Transdoc='DEFPMT' AND Description='Deferred Payment Plan Fee'", c);
        await using var row = await q.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct) || row.GetInt32(0) <= 0 || row.GetInt32(1) <= 0 || row.GetDecimal(2) != fee ||
            row.GetString(3) != "DEBIT" || row.GetString(4) != "Yes" || !row.GetBoolean(5))
            throw new InvalidOperationException("The deferred-payment fee accounting definition needs Student Billing review.");
        var result = (row.GetInt32(0), row.GetInt32(1));
        if (await row.ReadAsync(ct)) throw new InvalidOperationException("The payment-plan fee definition is ambiguous.");
        return result;
    }

    internal static decimal ValidateExisting(IReadOnlyList<(decimal Debit, decimal Credit)> rows, decimal fee)
    {
        if (rows.Count == 0) return 0;
        if (rows.Count != 1 || rows[0].Debit != fee || rows[0].Credit != 0)
            throw new InvalidOperationException("Existing payment-plan fees or credits need Student Billing review. No fee was added.");
        return fee;
    }

    public async Task<decimal> ExistingAmountAsync(int studentUid, int termId, decimal expectedFee, CancellationToken ct = default)
    {
        if (studentUid <= 0 || termId <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        if (expectedFee == 0) return 0;
        var definition = await Definition(expectedFee, ct);
        await using var c = await Open(ct);
        await using var q = new SqlCommand("""
            SELECT Debits,Credits FROM dbo.Billing WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDOCID=@Doc
              AND COALESCE(Voided,'No')<>'Yes' AND COALESCE(Reversing,'No')<>'Yes' AND (Debits<>0 OR Credits<>0)
            UNION ALL SELECT Debits,Credits FROM dbo.BillingBatch WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDOCID=@Doc AND (Debits<>0 OR Credits<>0);
            """, c) { CommandTimeout = 30 };
        q.Parameters.AddWithValue("@Student", studentUid); q.Parameters.AddWithValue("@Term", termId); q.Parameters.AddWithValue("@Doc", definition.Doc);
        var rows = new List<(decimal, decimal)>(); await using var reader = await q.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add((reader.IsDBNull(0) ? 0 : reader.GetDecimal(0), reader.IsDBNull(1) ? 0 : reader.GetDecimal(1)));
        return ValidateExisting(rows, expectedFee);
    }

    private async Task<string> Token(CancellationToken ct)
    {
        environmentGuard.EnsureWriteEnabled();
        using var request = new HttpRequestMessage(HttpMethod.Get, Setting("BaseUrl").TrimEnd('/') + "/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting("Username") + ":" + Setting("Password"))));
        using var response = await Client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["data"]?["token"]?.GetValue<string>() ?? throw new InvalidOperationException("Elements authentication failed.");
    }
    private async Task<JsonNode> Send(HttpMethod method, string route, string token, object? body, CancellationToken ct)
    {
        environmentGuard.EnsureWriteEnabled();
        using var request = new HttpRequestMessage(method, Setting("RegistrationBaseUrl").TrimEnd('/') + route);
        request.Headers.Authorization = new("Bearer", token); request.Headers.Add("TenantHost", Setting("TenantHost")); request.Headers.Add("Module", "Billing");
        request.Headers.Add("Origin", "https://" + Setting("TenantHost")); request.Headers.Referrer = new Uri("https://" + Setting("TenantHost"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, ct);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode || root?["isSuccess"]?.GetValue<bool>() != true || root["data"] is null)
            throw new InvalidOperationException("Elements did not confirm the payment-plan fee request. Review before retrying.");
        return root["data"]!;
    }
    private async Task<List<JsonObject>> Batches(string token, CancellationToken ct) =>
        (await Send(HttpMethod.Get, "/api/billing/cashier-entry/select-items", token, null, ct))["batchMasterList"]?.AsArray().OfType<JsonObject>().ToList()
        ?? throw new InvalidOperationException("Elements omitted cashier batches.");
    private async Task<List<JsonObject>> Entries(string token, int student, int batch, CancellationToken ct)
    {
        var rows = (await Send(HttpMethod.Get, $"/api/billing/cashier-entry/batches/batch-entries-list/{student}/{batch}", token, null, ct))["batchEntriesList"]?.AsArray().OfType<JsonObject>().ToList()
            ?? throw new InvalidOperationException("Elements omitted fee entries.");
        if (rows.Any(x => x["studentUID"]?.GetValue<int>() != student || x["batchMasterID"]?.GetValue<int>() != batch))
            throw new InvalidOperationException("Elements returned fee entries outside the requested student/batch.");
        return rows;
    }
    internal static bool ValidateBatch(string comment, string status, int sourceModule, bool termBased)
    {
        if (!string.Equals(comment.Trim(), BatchComment, StringComparison.OrdinalIgnoreCase) || sourceModule != 2 || !termBased)
            throw new InvalidOperationException("The payment-plan fee batch identity needs Student Billing review.");
        return status == "Open";
    }
    private async Task<int?> Batch(List<JsonObject> rows, CancellationToken ct)
    {
        var matches = rows.Where(x => string.Equals(x["comment"]?.GetValue<string>()?.Trim(), BatchComment, StringComparison.OrdinalIgnoreCase))
            .Select(x => x["batchMasterID"]?.GetValue<int>() ?? 0).Distinct().ToList();
        var open = new List<int>();
        foreach (var id in matches)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid fee batch identifier.");
            var found = false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await using var c = await Open(ct);
                await using var q = new SqlCommand("SELECT Comment,Status,SourceModuleID,TermBased FROM dbo.BatchMaster WHERE BatchMasterID=@Id", c) { CommandTimeout = 15 };
                q.Parameters.AddWithValue("@Id", id);
                await using var row = await q.ExecuteReaderAsync(ct);
                if (await row.ReadAsync(ct))
                {
                    if (ValidateBatch(row.GetString(0), row.GetString(1), row.GetInt32(2), Convert.ToBoolean(row.GetValue(3)))) open.Add(id);
                    found = true; break;
                }
                await Task.Delay(500, ct);
            }
            if (!found) throw new InvalidOperationException("The new fee batch is still updating. Reload your bill before retrying; no fee was added.");
        }
        if (open.Count > 1) throw new InvalidOperationException("Multiple open payment-plan fee batches need review.");
        return open.Count == 0 ? null : open[0];
    }
    public async Task EnsureAsync(int studentUid, int termId, decimal expectedFee, CancellationToken ct = default)
    {
        if (expectedFee == 0) return;
        var definition = await Definition(expectedFee, ct);
        if (await ExistingAmountAsync(studentUid, termId, expectedFee, ct) > 0) return;
        var token = await Token(ct);
        // Serialize first-use batch creation across all students and web instances.
        await using var pg = new NpgsqlConnection(configuration.GetConnectionString("Default")); await pg.OpenAsync(ct);
        await using var tx = await pg.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(534653,55)", pg, tx)) await gate.ExecuteNonQueryAsync(ct);
        var batch = await Batch(await Batches(token, ct), ct);
        if (batch is null)
        {
            await Send(HttpMethod.Post, "/api/billing/cashier-entry/batches/batch-master", token, new { campus = 0, comment = BatchComment, mod = 2, termBased = true }, ct);
            batch = await Batch(await Batches(token, ct), ct) ?? throw new InvalidOperationException("The fee batch creation could not be confirmed. No fee was sent.");
        }
        if (batch <= 0) throw new InvalidOperationException("Invalid fee batch identity.");
        await tx.CommitAsync(ct);
        var reference = $"CFPP-{termId}-{studentUid}";
        async Task<bool> Present()
        {
            var all = await Entries(token, studentUid, batch.Value, ct);
            var matching = all.Where(x => x["termCalendarID"]?.GetValue<int>() == termId &&
                (x["transDOCID"]?.GetValue<int>() == definition.Doc || x["refNo"]?.GetValue<string>() == reference)).ToList();
            if (matching.Count == 0) return false;
            if (matching.Count != 1 || matching[0]["refNo"]?.GetValue<string>() != reference ||
                matching[0]["transDOCID"]?.GetValue<int>() != definition.Doc || matching[0]["arTypeID"]?.GetValue<int>() != definition.Ar ||
                decimal.Parse(matching[0]["amount"]?.ToString() ?? "0", CultureInfo.InvariantCulture) != expectedFee)
                throw new InvalidOperationException("The existing fee entry needs review. No duplicate was added.");
            return true;
        }
        if (await Present()) return;
        // Recheck posting/distribution under the student-term lease immediately before write.
        if (await ExistingAmountAsync(studentUid, termId, expectedFee, ct) > 0) return;
        var date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")).Date;
        try
        {
            await Send(HttpMethod.Post, "/api/billing/batch/student/batch", token, new {
                batchMasterID = batch.Value, billingBatchID = 0, allClear = "", studentID = studentUid, termID = termId,
                transactionTypeID = "DEBIT", currencyTypeID = (int?)null, extendedDocID = 0, transDocID = definition.Doc,
                transactionDate = date, amount = expectedFee, currencyTypeAmt = (decimal?)null, arTypeID = definition.Ar,
                description = "Deferred Payment Plan Fee", paymentPlanID = 0, refNo = reference }, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        { if (await Present()) return; throw; } // Readback only: never retry the POST.
        if (!await Present()) throw new InvalidOperationException("The fee posting could not be confirmed. Review before retrying.");
    }
}
