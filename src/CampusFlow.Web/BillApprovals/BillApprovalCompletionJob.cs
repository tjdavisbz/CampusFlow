using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace CampusFlow.Web.BillApprovals;

public interface IBillApprovalCompletionOperations
{
    bool IsEnabled(BillCompletionStep step);
    Task<bool> IsCompleteAsync(BillApproval approval, BillCompletionStep step);
    Task ExecuteAsync(BillApproval approval, BillCompletionStep step);
}

public class BillApprovalCompletionJob(BillApprovalCompletionCoordinator coordinator, ICurrentTenant tenant)
    : AsyncBackgroundJob<BillApprovalCompletionJobArgs>, ITransientDependency
{
    public override async Task ExecuteAsync(BillApprovalCompletionJobArgs args)
    {
        if (args.TenantId==Guid.Empty || args.ApprovalId==Guid.Empty) throw new ArgumentException("Missing approval identity.");
        using(tenant.Change(args.TenantId)) await coordinator.RunAsync(args.ApprovalId);
    }
}

public class BillApprovalCompletionCoordinator(IRepository<BillApproval,Guid> approvals, ICurrentTenant tenant,
    IUnitOfWorkManager units, IBillApprovalCompletionOperations operations) : ITransientDependency
{
    private async Task<T> PersistAsync<T>(Guid id, Func<BillApproval,T> action)
    {
        using var unit=units.Begin(requiresNew:true,isTransactional:true);
        var approval=await approvals.GetAsync(id);
        if (!tenant.Id.HasValue || approval.TenantId!=tenant.Id || approval.AcceptedAt is null ||
            approval.Status is BillApprovalStatus.Draft or BillApprovalStatus.AwaitingPayment or BillApprovalStatus.Failed)
            throw new InvalidOperationException("An approved bill in the current tenant is required.");
        var previous=JsonSerializer.Serialize(approval.ExtraProperties);var oldStatus=approval.Status;
        var result=action(approval);
        if(previous!=JsonSerializer.Serialize(approval.ExtraProperties)||oldStatus!=approval.Status) await approvals.UpdateAsync(approval,autoSave:true);await unit.CompleteAsync();return result;
    }
    public async Task RunAsync(Guid id)
    {
        var run=Guid.NewGuid();BillApproval? snapshot=null;
        try
        {
            var started=await PersistAsync(id,a=>{snapshot=a;var p=BillApprovalCompletionProgress.Read(a);var ok=p.Begin(run,DateTime.UtcNow);if(ok)p.Save(a);return ok;});
            if(!started)return;
        }
        catch(Volo.Abp.Data.AbpDbConcurrencyException) { return; }
        try
        {
            foreach(var step in Enum.GetValues<BillCompletionStep>())
            {
                var enabled=operations.IsEnabled(step);
                var previouslyConfirmed=await operations.IsCompleteAsync(snapshot!,step);
                var claimed=await PersistAsync(id,a=>{
                    var p=BillApprovalCompletionProgress.Read(a);if(p.ActiveRunId!=run)return false;
                    if(p.Step(step).Status is BillCompletionStatus.Completed or BillCompletionStatus.Uncertain or BillCompletionStatus.Running)return false;
                    if(previouslyConfirmed) {p.Finish(step,BillCompletionStatus.Completed);p.Save(a);return false;}
                    if(!enabled) {p.Finish(step,BillCompletionStatus.Disabled,"This delivery step has not been enabled.");p.Save(a);return false;}
                    var ok=p.Start(step,DateTime.UtcNow);if(ok){p.Save(a);snapshot=a;}return ok;
                });
                if(!claimed)continue;
                var status=BillCompletionStatus.Completed;string? error=null;
                try { await operations.ExecuteAsync(snapshot!,step); }
                catch(BillCompletionPreparationException ex) {status=BillCompletionStatus.Failed;error=ex.Message;}
                catch(Exception) {status=BillApprovalCompletionProgress.IsEmail(step)?BillCompletionStatus.Uncertain:BillCompletionStatus.Failed;
                    error=BillApprovalCompletionProgress.IsEmail(step)?"Sending could not be confirmed. Check the mail service delivery logs before retrying.":"The operation did not complete. Check configuration and retry.";}
                await PersistAsync(id,a=>{var p=BillApprovalCompletionProgress.Read(a);if(p.ActiveRunId==run){p.Finish(step,status,error);p.Save(a);}return true;});
            }
        }
        finally
        {
            await PersistAsync(id,a=>{var p=BillApprovalCompletionProgress.Read(a);if(p.ActiveRunId==run){p.ActiveRunId=null;p.Save(a);}return true;});
        }
    }
}
