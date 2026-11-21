using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed partial class ElementsHousingBillingWriter
{
    private async Task<(List<BatchEntry> Entries, List<BatchEntry> Legacy)> ReadOwnedEntriesAsync(
        string token, int studentUid, int termId, int batchId, IReadOnlyCollection<int> documents, CancellationToken ct)
    {
        var entries = await GetEntriesAsync(token, studentUid, batchId, ct);
        var evidence = await _legacyEntries(studentUid, termId, batchId, ct);
        return ReviewOwnership(entries, evidence, studentUid, termId, batchId, documents);
    }

    internal static (List<BatchEntry> Entries, List<BatchEntry> Legacy) ReviewOwnership(
        IReadOnlyList<BatchEntry> entries, IReadOnlyList<BatchEntry> evidence,
        int studentUid, int termId, int batchId, IReadOnlyCollection<int> documents)
    {
        if (entries.Any(x => x.Id <= 0 || x.StudentUid != studentUid || x.BatchMasterId != batchId) ||
            entries.Select(x => x.Id).Distinct().Count() != entries.Count)
            throw new InvalidOperationException("The housing charge identities are ambiguous.");
        var references = new[] { Reference(termId, studentUid, "BASE"), Reference(termId, studentUid, "SINGLE") };
        var sameTerm = entries.Where(x => x.TermCalendarId == termId).ToList();
        if (sameTerm.Any(x => x.Reference.StartsWith("CFH-", StringComparison.OrdinalIgnoreCase) &&
                !references.Contains(x.Reference, StringComparer.OrdinalIgnoreCase)) ||
            sameTerm.Any(x => references.Contains(x.Reference, StringComparer.OrdinalIgnoreCase) && !documents.Contains(x.TransactionDocumentId)))
            throw new InvalidOperationException("An unrecognized CampusFlow housing reference requires review.");
        var scoped = sameTerm.Where(x => documents.Contains(x.TransactionDocumentId)).ToList();
        if (scoped.Any(x => x.Amount < 0)) throw new InvalidOperationException("A housing credit requires manual review.");
        var legacy = scoped.Where(x => string.IsNullOrWhiteSpace(x.Reference) && x.Amount > 0 && x.TransactionDocumentId == 137 &&
            x.AccountsReceivableTypeId == 8047 && x.Description.Equals("Housing",StringComparison.OrdinalIgnoreCase) && evidence.Contains(x)).ToList();
        // Only the single original base charge can be adopted. Never guess how to
        // associate duplicate imports, a single-room surcharge or manual adjustments.
        if (legacy.Count > 1 || legacy.Count == 1 && scoped.Any(x => x.Reference.Equals(references[0], StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Duplicate imported or CampusFlow base charges require manual review.");
        RequireOwnedReferences(scoped.Except(legacy).Select(x => x.Reference), references);
        if (references.Any(r => scoped.Count(x => x.Reference.Equals(r, StringComparison.OrdinalIgnoreCase)) > 1))
            throw new InvalidOperationException("Duplicate CampusFlow housing references require manual review.");
        return (scoped, legacy);
    }

    private async Task<List<BatchEntry>> ReadLegacyEntriesAsync(int studentUid, int termId, int batchId, CancellationToken ct)
    {
        if (termId != 269) return [];
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(ct);
        await using (var exists = new SqlCommand("SELECT OBJECT_ID('dbo.TPS_BillingBatch_Housing_10_28_26','U')", connection))
            if (await exists.ExecuteScalarAsync(ct) is null or DBNull) return [];
        // This preserved import is evidence, not a writable source. Require exact
        // entry identity, owner, term, money, description, account and timestamps.
        await using var command = new SqlCommand("""
            SELECT b.BillingBatchID,b.BatchMasterID,b.RefNo,b.TransDocID,b.Debits,
                b.OwnerUID,b.TermCalendarID,b.ARTypeID,b.Description
            FROM dbo.BillingBatch b
            JOIN dbo.TPS_BillingBatch_Housing_10_28_26 original
              ON original.BillingBatchID=b.BillingBatchID AND original.OwnerUID=b.OwnerUID
             AND original.BatchMasterID=b.BatchMasterID AND original.TermCalendarID=b.TermCalendarID
             AND original.TransDocID=b.TransDocID AND original.Debits=b.Debits
             AND original.Credits=b.Credits AND original.ShowAmount=b.ShowAmount
             AND original.Description=b.Description AND original.ARTypeID=b.ARTypeID
             AND original.InsertTime=b.InsertTime
             AND (original.UpdateTime=b.UpdateTime OR original.UpdateTime IS NULL AND b.UpdateTime IS NULL)
            WHERE b.OwnerUID=@Student AND b.TermCalendarID=@Term AND b.BatchMasterID=@Batch
              AND b.Credits=0 AND b.Debits>0 AND b.TransDocID=137 AND b.ARTypeID=8047 AND b.Description='Housing'
              AND NULLIF(LTRIM(RTRIM(b.RefNo)),'') IS NULL
              AND NULLIF(LTRIM(RTRIM(original.RefNo)),'') IS NULL
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@Student", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@Term", SqlDbType.Int).Value = termId;
        command.Parameters.Add("@Batch", SqlDbType.Int).Value = batchId;
        var result = new List<BatchEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt32(0),reader.GetInt32(1),
            reader.IsDBNull(2)?"":reader.GetString(2).Trim(),reader.GetInt32(3),reader.GetDecimal(4),
            reader.GetInt32(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetString(8).Trim()));
        return result;
    }

    private static HousingBillingReview BuildReview(int studentUid, int termId, IReadOnlyList<HousingChargeLine> desired,
        List<BatchEntry> entries, List<BatchEntry> legacy, IReadOnlyList<BatchEntry>? generated = null)
    {
        var all = entries.Concat(generated ?? []).OrderBy(x => x.BatchMasterId).ThenBy(x => x.Id).ToArray();
        var identity = JsonSerializer.Serialize(new { Policy="reviewed-housing-billing-v1", studentUid,termId,
            Entries=all, Legacy=legacy.Select(x=>x.Id).Order().ToArray(),Desired=desired });
        return new(studentUid,termId,all.Sum(x=>x.Amount),desired.Sum(x=>x.Amount),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }
}
