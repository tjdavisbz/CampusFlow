using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
namespace CampusFlow.MealPlanTaxes;
public sealed class ElementsMealPlanTaxReadModel(IConfiguration config):IElementsMealPlanTaxReadModel,ITransientDependency
{
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        MealPlanTaxEnvironment.Validate(config);
        var b=new SqlConnectionStringBuilder(config.GetConnectionString("ThesisElementsReadOnly")){ApplicationIntent=ApplicationIntent.ReadOnly};
        var c=new SqlConnection(b.ConnectionString);try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}
    }
    public async Task<bool> IsInScopeAsync(int termId,CancellationToken ct=default)
    {
        await using var c=await Open(ct);await using var q=new SqlCommand("""
            SELECT CASE WHEN t.Term >= cutoff.Term THEN 1 ELSE 0 END
            FROM dbo.TermCalendar t CROSS JOIN dbo.TermCalendar cutoff
            WHERE t.TermCalendarID=@Term AND LTRIM(RTRIM(cutoff.TextTerm))='Spring 2027';
            """,c);q.Parameters.Add("@Term",SqlDbType.Int).Value=termId;
        await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct)||rows.IsDBNull(0))throw new InvalidOperationException("The tax term cutoff could not be verified.");
        var included=rows.GetInt32(0)==1;if(await rows.ReadAsync(ct))throw new InvalidOperationException("The tax term cutoff is ambiguous.");return included;
    }
    public async Task<MealPlanTaxDestination> DestinationAsync(CancellationToken ct=default)
    {
        var legacy=config.GetValue<int>("MealPlanTax:BatchMasterId");
        var shared=config.GetValue<int>("MealPlans:SharedBatchMasterId");
        var batch=shared>0?shared:legacy;var document=config.GetValue<int>("MealPlanTax:TransactionDocumentId");
        if(batch<=0||document<=0)throw new InvalidOperationException("Configure the dedicated meal-tax batch and tax document before enabling tax.");
        await using var c=await Open(ct);await using var q=new SqlCommand("""
            SELECT COUNT(*) FROM dbo.BatchMaster WHERE BatchMasterID=@Batch
                AND Comment=@Comment AND Status='Open' AND SourceModuleID=2 AND TermBased=1;
            SELECT COALESCE(ARTypeID,0) FROM dbo.TransDoc WHERE TransDocID=@Doc
                AND LTRIM(RTRIM(TransDoc))='TAX' AND LTRIM(RTRIM(Description))='Texas State Sales Tax'
                AND CONVERT(varchar(10),ActiveFlag) IN ('1','Yes','True') AND ReportFlag='Yes';
            """,c);q.Parameters.AddWithValue("@Batch",batch);q.Parameters.AddWithValue("@Comment",shared>0?"Meal Plan Charges":MealPlanTaxEnvironment.BatchComment);q.Parameters.AddWithValue("@Doc",document);
        await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct)||rows.GetInt32(0)!=1)throw new InvalidOperationException("The dedicated meal-tax batch is not open or its identity changed.");
        await rows.NextResultAsync(ct);if(!await rows.ReadAsync(ct))throw new InvalidOperationException("The approved sales-tax transaction document is unavailable.");var ar=Convert.ToInt32(rows.GetValue(0));if(await rows.ReadAsync(ct))throw new InvalidOperationException("The tax document is ambiguous.");return new(batch,document,ar,shared,legacy!=batch?legacy:0);
    }
    public async Task<MealPlanTaxReportedCharge> ReadAsync(MealPlanTaxTarget target,MealPlanTaxDestination destination,CancellationToken ct=default)
    {
        target.Validate();await using var c=await Open(ct);await using var q=new SqlCommand("""
            SELECT COUNT(*) FROM (
                SELECT RefNo,Debits,Credits FROM dbo.Billing WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDocID=@Doc AND COALESCE(Voided,'')<>'Yes' AND COALESCE(Reversing,'')<>'Yes'
                UNION ALL SELECT RefNo,Debits,Credits FROM dbo.BillingBatch WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDocID=@Doc
            ) allTax WHERE (COALESCE(Debits,0)<>0 OR COALESCE(Credits,0)<>0) AND COALESCE(RefNo,'') NOT LIKE 'CFMT-%';
            SELECT 'Ledger:'+CAST(BillingID AS varchar(20)),COALESCE(Debits,0),COALESCE(Credits,0) FROM dbo.Billing
                WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDocID=@Doc AND RefNo=@Ref
                    AND COALESCE(Voided,'')<>'Yes' AND COALESCE(Reversing,'')<>'Yes';
            SELECT 'Batch:'+CAST(BillingBatchID AS varchar(20)),COALESCE(Debits,0),COALESCE(Credits,0),BatchMasterID FROM dbo.BillingBatch
                WHERE OwnerUID=@Student AND TermCalendarID=@Term AND TransDocID=@Doc AND RefNo=@Ref;
            """,c);q.Parameters.AddWithValue("@Student",target.StudentUid);q.Parameters.AddWithValue("@Term",target.TermId);q.Parameters.AddWithValue("@Doc",destination.DocumentId);q.Parameters.AddWithValue("@Ref",target.Reference);
        await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct)||rows.GetInt32(0)!=0)throw new InvalidOperationException("An existing sales-tax charge needs billing review; CampusFlow will not duplicate it.");
        string? posted=null,batch=null;await rows.NextResultAsync(ct);while(await rows.ReadAsync(ct)){if(posted is not null||Convert.ToDecimal(rows.GetValue(1))!=target.TaxAmount||Convert.ToDecimal(rows.GetValue(2))!=0)throw new InvalidOperationException("Posted tax differs from the reviewed amount.");posted=rows.GetString(0);}
        await rows.NextResultAsync(ct);while(await rows.ReadAsync(ct)){if(batch is not null||Convert.ToDecimal(rows.GetValue(1))!=target.TaxAmount||Convert.ToDecimal(rows.GetValue(2))!=0||rows.GetInt32(3)!=destination.BatchId)throw new InvalidOperationException("The tax batch amount or destination changed.");batch=rows.GetString(0);}
        if(posted is not null&&batch is not null)throw new InvalidOperationException("Duplicate tax references require billing review.");return new(posted,batch);
    }
}
