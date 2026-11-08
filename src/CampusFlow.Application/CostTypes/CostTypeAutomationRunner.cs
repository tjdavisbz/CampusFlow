using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace CampusFlow.CostTypes;

public class CostTypeAutomationRunner(IRepository<CostTypeConfiguration,Guid> configurations,
    IRepository<CostTypeRun,Guid> runs,ICostTypeElements elements,IUnitOfWorkManager uow,
    ICurrentTenant currentTenant,IGuidGenerator ids,IClock clock,CostTypeRunExecutor executor) : ITransientDependency
{
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken=default)
    {
        if(currentTenant.Id is not Guid tenant)return false;
        Guid runId;string rulesJson;
        try
        {
            using var claim=uow.Begin(requiresNew:true,isTransactional:true);
            var c=await configurations.FindAsync(x=>x.TenantId==tenant);
            if(c is null)return false;
            var schedule=CostTypeAutomationState.Read(c);var now=clock.Now.ToUniversalTime();
            if(!schedule.IsDue(now) || c.RunningRunId is not null)return false;
            elements.EnsureRunEnabled(); // Validates this environment's own flag, tenant API endpoints and replica.
            if((await CostTypeRunExecutor.BlockingRunsAsync(runs,tenant)).Count>0)
            {
                schedule.Pause("Paused: an interrupted or unconfirmed run needs review in Elements.");schedule.Save(c);
                await configurations.UpdateAsync(c,true);await claim.CompleteAsync();return false;
            }
            rulesJson=c.RulesJson;runId=ids.Create();
            // Empty cycles retain only a compact audit record, not another full copy of all rules.
            var run=new CostTypeRun(runId,tenant,c.RulesRevision,"[]","[]",null);run.MarkAutomatic();run.Start();
            c.SetRun(runId);schedule.Started(runId,now);schedule.Save(c);
            await runs.InsertAsync(run,true);await configurations.UpdateAsync(c,true);await claim.CompleteAsync();
        }
        catch(AbpDbConcurrencyException) {return false;} // Another instance or a manual run won the atomic claim.
        var handedOff=false;
        try
        {
            if(!await executor.AutomaticStillEnabledAsync(tenant,runId))
            {await executor.FinishAsync(runId,tenant,"Stopped",[],true,"Stopped before updates were sent.");return true;}
            var rules=JsonSerializer.Deserialize<List<CostTypeRule>>(rulesJson)!;
            var rows=await elements.PreviewAsync(rules,null,cancellationToken);
            var changes=rows.Where(x=>x.Action=="Would change").ToList();
            if(changes.Count==0)
            {await executor.FinishAsync(runId,tenant,"Completed",[],true,"No changes needed. Unmapped, invalid and duplicate records were not applied.");return true;}
            var candidates=new List<CostTypeCandidate>();
            foreach(var row in changes.Take(CostTypeAutomationState.BatchLimit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(!await executor.AutomaticStillEnabledAsync(tenant,runId))
                {await executor.FinishAsync(runId,tenant,"Stopped",[],true,"Stopped before updates were sent.");return true;}
                candidates.Add(new(row,await elements.FingerprintAsync(row,cancellationToken)));
            }
            using(var prepare=uow.Begin(requiresNew:true,isTransactional:true))
            {
                var run=await runs.GetAsync(runId);if(run.TenantId!=tenant)throw new InvalidOperationException("Run tenant mismatch.");
                run.Prepare(rulesJson,JsonSerializer.Serialize(candidates));await runs.UpdateAsync(run,true);await prepare.CompleteAsync();
            }
            handedOff=true;await executor.ExecuteAsync(runId,tenant,candidates,rules,null,true,changes.Count,cancellationToken);
        }
        catch when(!handedOff)
        {
            await executor.FinishAsync(runId,tenant,"Failed before changes",[],true,
                "No updates sent: preview or verification could not complete. The schedule will check again later.");throw;
        }
        return true;
    }
}
