using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.MealPlanTaxes;

public sealed record MealPlanBatchSnapshot(int BaseDocument, IReadOnlyList<MealPlanRemovalEntry> Entries,
    IReadOnlyList<int> Batches);
public interface IElementsMealPlanBatchReadModel
{
    Task<MealPlanBatchSnapshot> ReadAsync(MealPlanTaxTarget target, MealPlanTaxDestination destination, CancellationToken ct);
}

public sealed class ElementsMealPlanBatchReadModel(IConfiguration config) : IElementsMealPlanBatchReadModel, ITransientDependency
{
    public async Task<MealPlanBatchSnapshot> ReadAsync(MealPlanTaxTarget target, MealPlanTaxDestination destination, CancellationToken ct)
    {
        MealPlanTaxEnvironment.Validate(config); target.Validate();
        if(destination.SharedBatchId <= 0 || destination.BatchId != destination.SharedBatchId)
            throw new InvalidOperationException("A verified shared meal-plan batch is required.");
        await using var connection = new SqlConnection(config.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.TransDocID, p.Amount, p.MealPlanName FROM StudentLife.MealPlans p
            JOIN StudentLife.MealPlanStudentAssignments a ON a.MealPlanID=p.MealPlanID
            WHERE a.StudentUID=@Student AND a.AssignmentID=@Assignment AND p.MealPlanID=@Plan AND p.TermCalendarID=@Term;
            """, connection);
        command.Parameters.AddWithValue("@Student",target.StudentUid); command.Parameters.AddWithValue("@Assignment",target.AssignmentId);
        command.Parameters.AddWithValue("@Plan",target.MealPlanId); command.Parameters.AddWithValue("@Term",target.TermId);
        int document; string name;
        await using(var reader=await command.ExecuteReaderAsync(ct))
        {
            if(!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1) || Convert.ToDecimal(reader.GetValue(1))!=target.BaseAmount)
                throw new InvalidOperationException("The meal assignment or price changed before batching.");
            document=reader.GetInt32(0); name=reader.GetString(2).Trim();
            if(document<=0 || document==destination.DocumentId || await reader.ReadAsync(ct))
                throw new InvalidOperationException("The meal billing identity is ambiguous.");
        }
        await using var query=new SqlCommand("""
            SELECT COALESCE(Debits,0),COALESCE(Credits,0) FROM dbo.Billing
            WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDocID=@Doc
              AND (COALESCE(Debits,0)<>0 OR COALESCE(Credits,0)<>0)
              AND COALESCE(Voided,'')<>'Yes' AND COALESCE(Reversing,'')<>'Yes';
            SELECT b.BillingBatchID,b.BatchMasterID,b.TransDocID,COALESCE(b.ARTypeID,0),COALESCE(b.Debits,0),COALESCE(b.Credits,0),
                COALESCE(b.RefNo,''),COALESCE(b.Description,''),COALESCE(m.Comment,''),COALESCE(m.Status,''),COALESCE(m.SourceModuleID,0),COALESCE(m.TermBased,0)
            FROM dbo.BillingBatch b LEFT JOIN dbo.BatchMaster m ON m.BatchMasterID=b.BatchMasterID
            WHERE b.OwnerUID=@Student AND b.TermCalendarID=@Term AND b.TransDocID IN (@Doc,@Tax)
                AND (COALESCE(b.Debits,0)<>0 OR COALESCE(b.Credits,0)<>0);
            SELECT BatchMasterID FROM dbo.BatchMaster WHERE Status='Open' AND TermBased=1 AND
                (Comment='Meal Plan Charges' AND SourceModuleID=15 OR
                 BatchMasterID=@Legacy AND Comment='CampusFlow meal-plan sales tax' AND SourceModuleID=2);
            """,connection);
        query.Parameters.AddWithValue("@Student",target.StudentUid); query.Parameters.AddWithValue("@Term",target.TermId);
        query.Parameters.AddWithValue("@Doc",document); query.Parameters.AddWithValue("@Tax",destination.DocumentId);
        query.Parameters.AddWithValue("@Legacy",destination.LegacyBatchId);
        await using var rows=await query.ExecuteReaderAsync(ct);
        var posted=0;
        while(await rows.ReadAsync(ct))
        {
            if(++posted>1 || Convert.ToDecimal(rows.GetValue(0))!=target.BaseAmount || Convert.ToDecimal(rows.GetValue(1))!=0)
                throw new InvalidOperationException("Posted meal charges need a billing review before batching.");
        }
        await rows.NextResultAsync(ct); var reported=new List<ElementsMealPlanRemovalGateway.ReportedRow>();
        while(await rows.ReadAsync(ct)) reported.Add(new(rows.GetInt32(0),rows.GetInt32(1),rows.GetInt32(2),rows.GetInt32(3),
            Convert.ToDecimal(rows.GetValue(4)),Convert.ToDecimal(rows.GetValue(5)),rows.GetString(6).Trim(),rows.GetString(7).Trim(),
            rows.GetString(8).Trim(),rows.GetString(9).Trim(),rows.GetInt32(10),Convert.ToBoolean(rows.GetValue(11))));
        var entries=ElementsMealPlanRemovalGateway.Classify(new(target.StudentUid,target.TermId,target.AssignmentId,target.MealPlanId,name,target.BaseAmount),document,destination,reported);
        if(posted+entries.Count(x=>x.Kind=="meal plan")!=1)
            throw new InvalidOperationException("Exactly one matching meal-plan base charge must be confirmed before batching.");
        var batches=new HashSet<int>{destination.BatchId};
        await rows.NextResultAsync(ct); while(await rows.ReadAsync(ct)) batches.Add(rows.GetInt32(0));
        foreach(var entry in entries) batches.Add(entry.BatchId);
        if(batches.Count>100) throw new InvalidOperationException("Too many meal batches require a billing review.");
        return new(document,entries,batches.Order().ToList());
    }
}
