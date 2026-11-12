using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.MealPlanTaxes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Housing;

public sealed partial class ElementsMealPlanRemovalGateway
{
    internal sealed record ReportedRow(int Id, int Batch, int Document, int ArType, decimal Debit, decimal Credit,
        string Reference, string Description, string BatchComment, string BatchStatus, int SourceModule, bool TermBased);
    private sealed record ReportedReview(int BaseDocument, MealPlanTaxDestination Tax, List<int> Batches, List<MealPlanRemovalEntry> Entries);
    internal static List<MealPlanRemovalEntry> Classify(MealPlanRemovalTarget target, int baseDocument,
        MealPlanTaxDestination tax, IReadOnlyList<ReportedRow> rows)
    {
        var entries = new List<MealPlanRemovalEntry>();
        foreach (var row in rows)
        {
            var isBase = row.Document == baseDocument && row.Debit == target.Amount && target.Amount > 0 &&
                row.ArType == 0 && row.Reference == "" && row.Description == "Meal Plan" && row.BatchComment == "Meal Plan Charges" && row.SourceModule == 15;
            var isTax = row.Document == tax.DocumentId && row.Batch == tax.BatchId && row.ArType == tax.ArTypeId &&
                row.Reference == $"CFMT-{target.TermId}-{target.StudentUid}-{target.AssignmentId}" &&
                row.Debit == MealPlanTaxRules.Amount(target.Amount) && row.Debit > 0 && row.Description == "Meal plan sales tax (8.25%)" && row.BatchComment == MealPlanTaxEnvironment.BatchComment && row.SourceModule == 2;
            if (row.Id <= 0 || row.Batch <= 0 || row.Credit != 0 || row.BatchStatus != "Open" || !row.TermBased || !isBase && !isTax)
                throw new InvalidOperationException("A manual, changed or unrelated meal/tax charge needs Business Services review. No further removal was sent.");
            entries.Add(new(row.Id, row.Batch, row.Document, row.ArType, row.Debit, row.Reference, row.Description, isBase ? "meal plan" : "sales tax"));
        }
        if (entries.Select(x => x.Id).Distinct().Count() != entries.Count || entries.GroupBy(x => x.Kind).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Multiple matching meal/tax charges require Business Services review before removal.");
        return entries;
    }
    private async Task<ReportedReview> Reported(MealPlanRemovalTarget target)
    {
        Guard(target); var tax = await _tax.DestinationAsync();
        await using var connection = new SqlConnection(_config.GetConnectionString("ThesisElementsReadOnly")); await connection.OpenAsync();
        int baseDocument;
        await using (var lookup = new SqlCommand("SELECT TransDocID,Amount,MealPlanName FROM StudentLife.MealPlans WHERE MealPlanID=@Plan AND TermCalendarID=@Term", connection))
        {
            lookup.Parameters.AddWithValue("@Plan", target.MealPlanId); lookup.Parameters.AddWithValue("@Term", target.TermId);
            await using var r = await lookup.ExecuteReaderAsync();
            if (!await r.ReadAsync() || r.IsDBNull(0) || r.IsDBNull(1) || r.GetDecimal(1) != target.Amount || r.GetString(2).Trim() != target.Name)
                throw new InvalidOperationException("The meal-plan price, term or document changed. Review the assignment again.");
            baseDocument = r.GetInt32(0); if (baseDocument <= 0 || baseDocument == tax.DocumentId || await r.ReadAsync()) throw new InvalidOperationException("Meal-plan billing identity is ambiguous.");
        }
        await using var q = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.Billing WHERE OwnerUID=@Student AND TermCalendarID=@Term
                AND TransDocID IN (@BaseDoc,@TaxDoc) AND (COALESCE(Debits,0)<>0 OR COALESCE(Credits,0)<>0)
                AND COALESCE(Voided,'')<>'Yes' AND COALESCE(Reversing,'')<>'Yes';
            SELECT b.BillingBatchID,b.BatchMasterID,b.TransDocID,COALESCE(b.ARTypeID,0),COALESCE(b.Debits,0),COALESCE(b.Credits,0),
                COALESCE(b.RefNo,''),COALESCE(b.Description,''),COALESCE(m.Comment,''),COALESCE(m.Status,''),COALESCE(m.SourceModuleID,0),COALESCE(m.TermBased,0)
            FROM dbo.BillingBatch b LEFT JOIN dbo.BatchMaster m ON m.BatchMasterID=b.BatchMasterID
            WHERE b.OwnerUID=@Student AND b.TermCalendarID=@Term AND b.TransDocID IN (@BaseDoc,@TaxDoc)
                AND (COALESCE(b.Debits,0)<>0 OR COALESCE(b.Credits,0)<>0);
            SELECT BatchMasterID FROM dbo.BatchMaster WHERE Comment='Meal Plan Charges' AND Status='Open' AND SourceModuleID=15 AND TermBased=1;
            """, connection) { CommandTimeout = 60 };
        q.Parameters.Add("@Student", SqlDbType.Int).Value = target.StudentUid; q.Parameters.Add("@Term", SqlDbType.Int).Value = target.TermId;
        q.Parameters.Add("@BaseDoc", SqlDbType.Int).Value = baseDocument; q.Parameters.Add("@TaxDoc", SqlDbType.Int).Value = tax.DocumentId;
        await using var rows = await q.ExecuteReaderAsync();
        if (!await rows.ReadAsync() || rows.GetInt32(0) != 0)
            throw new InvalidOperationException("A meal-plan or tax charge is already posted. Student Billing must review an adjustment; this removal will not delete posted transactions.");
        await rows.NextResultAsync(); var reported = new List<ReportedRow>();
        while (await rows.ReadAsync()) reported.Add(new(rows.GetInt32(0), rows.GetInt32(1), rows.GetInt32(2), rows.GetInt32(3),
            Convert.ToDecimal(rows.GetValue(4)), Convert.ToDecimal(rows.GetValue(5)), rows.GetString(6).Trim(), rows.GetString(7).Trim(), rows.GetString(8).Trim(), rows.GetString(9).Trim(), rows.GetInt32(10), Convert.ToBoolean(rows.GetValue(11))));
        var entries = Classify(target, baseDocument, tax, reported);
        await rows.NextResultAsync(); var batches = new HashSet<int> { tax.BatchId };
        while (await rows.ReadAsync()) batches.Add(rows.GetInt32(0));
        foreach (var entry in entries) batches.Add(entry.BatchId);
        if (batches.Count > 100) throw new InvalidOperationException("Too many open meal-plan batches require a billing review.");
        return new(baseDocument, tax, batches.Order().ToList(), entries);
    }
}
