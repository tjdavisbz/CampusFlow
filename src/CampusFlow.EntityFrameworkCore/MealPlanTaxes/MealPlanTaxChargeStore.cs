using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
namespace CampusFlow.MealPlanTaxes;
public sealed class MealPlanTaxChargeStore(IRepository<MealPlanTaxCharge,Guid> charges,IUnitOfWorkManager units,
    ICurrentTenant tenant,IGuidGenerator ids) : IMealPlanTaxChargeStore,ITransientDependency
{
    public async Task<MealPlanTaxClaim> ClaimAsync(MealPlanTaxTarget target,CancellationToken ct=default)
    {
        target.Validate();var owner=tenant.Id??throw new InvalidOperationException("A tenant is required.");
        try
        {
            using var claim=units.Begin(requiresNew:true,isTransactional:true);
            var existing=await charges.FindAsync(x=>x.TenantId==owner&&x.AssignmentId==target.AssignmentId,cancellationToken:ct);
            if(existing is not null){if(!existing.Matches(target))throw new UserFriendlyException("The saved meal-tax amount or assignment changed. Student Billing must review it.");return new(existing.Id,false);}
            var row=new MealPlanTaxCharge(ids.Create(),owner,target);
            await charges.InsertAsync(row,true,ct);await claim.CompleteAsync(ct);return new(row.Id,true);
        }
        catch(DbUpdateException error) when(error.InnerException is Npgsql.PostgresException {SqlState:"23505"} || IsSqliteUnique(error.InnerException))
        {
            // A competing request owns the one durable attempt. It alone may send.
            using var read=units.Begin(requiresNew:true,isTransactional:false);
            var existing=await charges.FindAsync(x=>x.TenantId==owner&&x.AssignmentId==target.AssignmentId,cancellationToken:ct)
                ?? throw new InvalidOperationException("The competing meal-tax attempt could not be read.");
            if(!existing.Matches(target))throw new UserFriendlyException("The meal-tax assignment changed during review.");
            return new(existing.Id,false);
        }
    }
    public async Task VerifyAsync(Guid id,string externalEntryId,CancellationToken ct=default)
    {
        using var change=units.Begin(requiresNew:true,isTransactional:true);var row=await Owned(id,ct);
        row.Verify(externalEntryId);await charges.UpdateAsync(row,true,ct);await change.CompleteAsync(ct);
    }
    public async Task MarkUncertainAsync(Guid id,CancellationToken ct=default)
    {
        using var change=units.Begin(requiresNew:true,isTransactional:true);var row=await Owned(id,ct);
        row.Uncertain();await charges.UpdateAsync(row,true,ct);await change.CompleteAsync(ct);
    }
    private async Task<MealPlanTaxCharge> Owned(Guid id,CancellationToken ct)
    {
        var row=await charges.GetAsync(id,cancellationToken:ct);
        if(!tenant.Id.HasValue||row.TenantId!=tenant.Id)throw new InvalidOperationException("Meal-tax tenant mismatch.");return row;
    }
    private static bool IsSqliteUnique(Exception? error)=>error?.GetType().FullName=="Microsoft.Data.Sqlite.SqliteException" &&
        error.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(error) is int code && code is 2067 or 1555;
}
