using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
namespace CampusFlow.ClearanceResets;
public class ClearanceResetRunner(IRepository<ClearanceResetConfiguration,Guid> configurations,
    IRepository<ClearanceResetRun,Guid> runs,IClearanceResetElements elements,IUnitOfWorkManager units,
    ICurrentTenant tenant,IGuidGenerator ids,IClock clock):ITransientDependency
{
    private DateTime Now=>clock.Now.ToUniversalTime();
    public static async Task<List<ClearanceResetRun>> Blockers(IRepository<ClearanceResetRun,Guid> runs,Guid tenant)
    {var rows=await runs.GetListAsync(x=>x.TenantId==tenant&&(x.State=="Running"||x.State=="Needs review"));return rows.Where(x=>x.State=="Running"||!x.Reviewed).ToList();}
    public async Task<bool> RunIfDueAsync(CancellationToken ct=default)
    {
        if(tenant.Id is not Guid owner)return false;
        Guid runId;
        try
        {
            using var claim=units.Begin(requiresNew:true,isTransactional:true);
            var config=await configurations.FindAsync(x=>x.TenantId==owner);if(config is null)return false;
            var state=config.Schedule();
            if(state.Enabled&&state.PolicyVersion!=ClearanceResetSchedule.CurrentPolicyVersion){state.Pause("Policy changed: review a fresh preview before enabling the revised reset rule.");config.Save(state);await configurations.UpdateAsync(config,true);await claim.CompleteAsync();return false;}
            if(!state.IsDue(Now)||config.RunningRunId.HasValue)return false;
            elements.EnsureRunEnabled();
            if((await Blockers(runs,owner)).Count>0)
            {state.Pause("Paused: a previous clearance reset needs administrator review.");config.Save(state);await configurations.UpdateAsync(config,true);await claim.CompleteAsync();return false;}
            runId=ids.Create();var automaticRun=new ClearanceResetRun(runId,owner);automaticRun.MarkAutomatic();await runs.InsertAsync(automaticRun,true);config.SetRun(runId);state.Started(Now);config.Save(state);
            await configurations.UpdateAsync(config,true);await claim.CompleteAsync();
        }
        catch(AbpDbConcurrencyException){return false;}
        await ExecuteClaimedAsync(owner,runId,null,true,ct);return true;
    }
    // Shared internal executor; this class does not expose an application-service endpoint.
    public async Task<List<ClearanceResetResult>> ExecuteClaimedAsync(Guid owner,Guid runId,List<ClearanceResetCandidate>? reviewedCandidates,bool automatic,CancellationToken ct=default)
    {
        var results=new List<ClearanceResetResult>();var outcome="Completed";var summary="";var more=0;
        try
        {
            var candidates=reviewedCandidates??await elements.FindCandidatesAsync(ct);more=Math.Max(0,candidates.Count-ClearanceResetSchedule.BatchLimit);
            foreach(var candidate in candidates.Take(ClearanceResetSchedule.BatchLimit))
            {
                ct.ThrowIfCancellationRequested();
                if(automatic&&!await StillEnabled(owner,runId)){outcome="Stopped";break;}
                try
                {
                    results.Add(await elements.ResetIfEligibleAsync(candidate,async attempt=>
                    {
                        using var record=units.Begin(requiresNew:true,isTransactional:true);
                        var c=await configurations.FindAsync(x=>x.TenantId==owner);
                        if(c?.RunningRunId!=runId||(automatic&&!c.Schedule().Enabled))return false;
                        var run=await runs.GetAsync(runId);if(run.TenantId!=owner)throw new InvalidOperationException("Run tenant mismatch.");
                        run.RecordAttempt(attempt);await runs.UpdateAsync(run,true);await record.CompleteAsync();return true;
                    },ct));
                }
                catch(ClearanceResetUnconfirmedException)
                {outcome="Needs review";results.Add(new(candidate.StudentUid,candidate.TermId,candidate.Term,"Unconfirmed: check Elements before retrying."));}
                using var progress=units.Begin(requiresNew:true,isTransactional:true);var history=await runs.GetAsync(runId);history.RecordResults(results);
                await runs.UpdateAsync(history,true);await progress.CompleteAsync();if(outcome=="Needs review")break;
            }
        }
        catch {if(outcome!="Needs review"){outcome="Failed before next change";summary="A source check failed before the next update was sent. Create a fresh preview or allow a later scheduled check.";}else summary="An unconfirmed update and a tracking failure need administrator review before retrying.";throw;}
        finally
        {
            using var finish=units.Begin(requiresNew:true,isTransactional:true);var run=await runs.GetAsync(runId);
            if(run.TenantId!=owner)throw new InvalidOperationException("Run tenant mismatch.");
            if(summary.Length==0)summary=$"{outcome}: {results.Count(x=>x.Outcome=="Verified blank")} verified resets; {results.Count(x=>x.Outcome!="Verified blank")} skipped or unconfirmed.";
            if(more>0)summary+=$" Up to {more} additional candidates remain for a later cycle.";
            run.RecordResults(results);run.Finish(outcome,summary);await runs.UpdateAsync(run,true);
            var c=await configurations.FindAsync(x=>x.TenantId==owner);
            if(c?.RunningRunId==runId){c.SetRun(null);var state=c.Schedule();state.Finished(Now,summary,outcome=="Needs review");c.Save(state);await configurations.UpdateAsync(c,true);}
            await finish.CompleteAsync();
        }
        return results;
    }
    private async Task<bool> StillEnabled(Guid owner,Guid runId)
    {using var read=units.Begin(requiresNew:true,isTransactional:false);var c=await configurations.FindAsync(x=>x.TenantId==owner);var enabled=c?.RunningRunId==runId&&c.Schedule().Enabled;await read.CompleteAsync();return enabled;}
}
