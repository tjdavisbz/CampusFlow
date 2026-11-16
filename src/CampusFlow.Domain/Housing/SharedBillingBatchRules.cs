using System;
using System.Collections.Generic;
using System.Linq;
namespace CampusFlow.Housing;

public sealed record SharedBillingBatchIdentity(int Id,string Comment,string Status,int SourceModule,bool TermBased);
public static class SharedBillingBatchRules
{
    public static int Resolve(int configured,string comment,IReadOnlyCollection<SharedBillingBatchIdentity> rows,bool requireOpen=true)
    {
        if(configured<=0||string.IsNullOrWhiteSpace(comment))throw new InvalidOperationException("A dedicated billing batch must be configured.");
        if(rows.Any(x=>x.Id<=0)||rows.Select(x=>x.Id).Distinct().Count()!=rows.Count)
            throw new InvalidOperationException("The shared billing batch identity is ambiguous.");
        var original=rows.SingleOrDefault(x=>x.Id==configured);
        bool Matches(SharedBillingBatchIdentity row)=>row.Comment==comment&&row.SourceModule==2&&row.TermBased;
        if(original is not null&&!Matches(original))throw new InvalidOperationException("The configured billing batch identity changed. Student Billing must review it.");
        if(original?.Status=="Open")return original.Id;
        var replacements=rows.Where(x=>Matches(x)&&x.Status=="Open").ToList();
        if(replacements.Count>1)throw new InvalidOperationException("Multiple open shared billing batches require Student Billing review.");
        if(replacements.Count==1)return replacements[0].Id;
        // Removing an assignment/charge does not require a destination for new charges.
        if(!requireOpen)return configured;
        throw new InvalidOperationException($"Prepare one open, term-based cashier batch with the comment '{comment}'. Elements can remove an empty batch after distribution; do not recreate the student's assignment.");
    }
}
