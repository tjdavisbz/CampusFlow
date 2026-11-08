using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace CampusFlow.CostTypes;

// Internal execution path shared by manual and scheduled runs; not an application-service endpoint.
public class CostTypeRunExecutor(IRepository<CostTypeConfiguration,Guid> configurations,
    IRepository<CostTypeRun,Guid> runs,ICostTypeElements elements,IUnitOfWorkManager uow,IClock? clock=null) : ITransientDependency
{
    private DateTime UtcNow => clock?.Now.ToUniversalTime() ?? DateTime.UtcNow;
    public static async Task<List<CostTypeRun>> BlockingRunsAsync(IRepository<CostTypeRun,Guid> runs,Guid tenant)
    {
        var safe=new[]{"Preview","Completed","Completed with skips","Failed before changes","Stopped"};
        var list=await runs.GetListAsync(x=>x.TenantId==tenant && !safe.Contains(x.State)) ?? [];
        return list.Where(x=>x.State=="Running" || !x.IsReviewed).OrderBy(x=>x.CreationTime).ToList();
    }
    public async Task<bool> AutomaticStillEnabledAsync(Guid tenant,Guid runId)
    {
        using var unit=uow.Begin(requiresNew:true,isTransactional:false);
        var c=await configurations.FindAsync(x=>x.TenantId==tenant);
        var enabled=c is not null && c.RunningRunId==runId && CostTypeAutomationState.Read(c).Enabled;
        await unit.CompleteAsync();return enabled;
    }
    public async Task<List<CostTypeResultDto>> ExecuteAsync(Guid runId,Guid tenant,List<CostTypeCandidate> candidates,
        List<CostTypeRule> rules,int? studentUid,bool automatic,int totalChanges=0,CancellationToken cancellationToken=default)
    {
        var results=new List<CostTypeResultDto>();var state="Completed";var writeAttempted=false;
        try
        {
            if(automatic && !await AutomaticStillEnabledAsync(tenant,runId)) {state="Stopped";return results;}
            var fresh=await elements.PreviewAsync(rules,studentUid,cancellationToken);
            foreach(var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(automatic && !await AutomaticStillEnabledAsync(tenant,runId)) {state="Stopped";break;}
                var row=candidate.Row;
                var current=fresh.SingleOrDefault(x=>x.StatusId==row.StatusId && x.StudentUid==row.StudentUid && x.TermId==row.TermId);
                var outcome="Skipped: source record changed; preview again.";
                if(current==row)
                {
                    try
                    {
                        // Persisted run ownership is already held. An exception from Apply can mean a partial write.
                        writeAttempted=true;await elements.ApplyAsync(candidate,cancellationToken);outcome="Verified updated";
                    }
                    catch(Exception) {outcome="Unconfirmed: verify in Elements before retrying.";state="Needs review";}
                }
                results.Add(new(row.StudentUid,row.TermId,outcome));
                using var progress=uow.Begin(requiresNew:true,isTransactional:true);
                var run=await runs.GetAsync(runId);if(run.TenantId!=tenant)throw new InvalidOperationException("Run tenant mismatch.");
                run.Record(JsonSerializer.Serialize(results));await runs.UpdateAsync(run,true);await progress.CompleteAsync();
                if(state=="Needs review")break;
            }
        }
        catch {state=automatic && !writeAttempted?"Failed before changes":"Needs review";throw;}
        finally
        {
            if(state=="Completed" && results.Any(x=>x.Outcome!="Verified updated"))state="Completed with skips";
            var updated=results.Count(x=>x.Outcome=="Verified updated");
            var summary=state=="Failed before changes"?"No updates sent: the source could not be confirmed. The schedule will check again later.":
                $"{state}: {updated} verified updates, {results.Count-updated} skipped or unconfirmed.";
            if(automatic && totalChanges>candidates.Count)summary+=$" Up to {totalChanges-candidates.Count} additional changes remain for a later cycle.";
            await FinishAsync(runId,tenant,state,results,automatic,summary);
        }
        return results;
    }
    public async Task FinishAsync(Guid runId,Guid tenant,string state,List<CostTypeResultDto> results,bool automatic,string summary)
    {
        using var finish=uow.Begin(requiresNew:true,isTransactional:true);
        var run=await runs.GetAsync(runId);if(run.TenantId!=tenant)throw new InvalidOperationException("Run tenant mismatch.");
        run.Finish(state);run.Record(JsonSerializer.Serialize(results));run.SetSummary(summary);await runs.UpdateAsync(run,true);
        var c=await configurations.FindAsync(x=>x.TenantId==tenant);
        if(c?.RunningRunId==runId)
        {
            c.SetRun(null);var schedule=CostTypeAutomationState.Read(c);
            if(automatic)schedule.Finished(UtcNow,summary,state=="Needs review");
            else if(state=="Needs review" && schedule.Enabled)schedule.Pause("Paused: a manual run has an unconfirmed update that needs review.");
            schedule.Save(c);await configurations.UpdateAsync(c,true);
        }
        await finish.CompleteAsync();
    }
}
