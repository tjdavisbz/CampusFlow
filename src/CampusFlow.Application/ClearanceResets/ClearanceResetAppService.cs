using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
namespace CampusFlow.ClearanceResets;
[Authorize(CampusFlowPermissions.Admin.BillApproval)]
public class ClearanceResetAppService(IRepository<ClearanceResetConfiguration,Guid> configurations,
    IRepository<ClearanceResetRun,Guid> runs,IClearanceResetElements elements,IUnitOfWorkManager units,ClearanceResetRunner runner,IClearanceResetProtection? protection=null):CampusFlowAppService,IClearanceResetAppService
{
    private Guid Tenant()=>CurrentTenant.Id??throw new UserFriendlyException("Select a university first.");
    private static ClearanceResetRunDto Dto(ClearanceResetRun x)=>new(x.Id,x.CreationTime,x.State,x.Summary,x.Reviewed,
        (JsonSerializer.Deserialize<List<ClearanceResetResult>>(x.ResultsJson)??[]).Select(r=>new ClearanceResetResultDto(r.StudentUid,r.TermId,r.Term,r.Outcome)).ToList(),x.Automatic);
    [DisableAuditing]
    public async Task<ClearanceResetSettingsDto> GetAsync()
    {
        var tenant=Tenant();var c=await configurations.FindAsync(x=>x.TenantId==tenant);var state=c?.Schedule()??new();
        bool ready;try{elements.EnsureRunEnabled();ready=true;}catch(InvalidOperationException){ready=false;}
        var query=await runs.GetQueryableAsync();var recent=await AsyncExecuter.ToListAsync(query.Where(x=>x.TenantId==tenant&&x.State!="Preview").OrderByDescending(x=>x.CreationTime).Take(10));
        return new(){ManualExceptions=(c?.Exceptions()??[]).Select(x=>new ManualClearanceExceptionDto(x.StudentUid,x.TermId,x.Term,x.Reason,x.CreatedAtUtc)).ToList(),Enabled=state.Enabled&&state.PolicyVersion==2,CanEnable=ready,RunningRunId=c?.RunningRunId,NextRunAtUtc=state.NextRunAtUtc,LastStartedAtUtc=state.LastStartedAtUtc,
            LastFinishedAtUtc=state.LastFinishedAtUtc,LastOutcome=state.Enabled&&state.PolicyVersion!=2?"The reset policy changed. Review a fresh preview, then explicitly enable the revised schedule.":state.LastOutcome,RecentRuns=recent.Select(Dto).ToList(),ReviewRuns=(await ClearanceResetRunner.Blockers(runs,tenant)).Select(Dto).ToList()};
    }
    [DisableAuditing]
    public async Task<List<ManualClearanceTermDto>> GetExceptionTermsAsync()
    {Tenant();return (await elements.GetTermsAsync()).Select(x=>new ManualClearanceTermDto(x.TermId,x.Term)).ToList();}
    [DisableAuditing]
    public async Task<ManualClearanceTargetDto> PreviewExceptionAsync(int studentUid,int termId)
    {
        Tenant();if(CurrentUser.Id is null)throw new UserFriendlyException("Sign in normally before reviewing a manual exception.");
        var target=await elements.GetTargetAsync(studentUid,termId);
        if(target is null||target.StudentUid!=studentUid||target.TermId!=termId||target.StatusId<=0)throw new UserFriendlyException("The student term status could not be verified.");
        return new(target.StudentUid,target.TermId,target.StatusId,target.StudentId,target.Name,target.Term);
    }
    [UnitOfWork(IsDisabled=true)]
    public async Task<ClearanceResetSettingsDto> SetExceptionAsync(ManualClearanceExceptionInput input)
    {
        var owner=Tenant();var actor=CurrentUser.Id??throw new UserFriendlyException("Sign in normally before changing an exception.");
        if(actor==Guid.Empty)throw new UserFriendlyException("Sign in normally before changing an exception.");
        var reason=input.Reason?.Trim()??"";
        if(!input.Confirmed||input.StudentUid<=0||input.TermId<=0||reason.Length is <10 or >1000)
            throw new UserFriendlyException("Review and confirm the student and term, and enter a reason of 10–1000 characters.");
        if(protection is null)throw new InvalidOperationException("Clearance protection is unavailable.");
        await using var lease=await protection.AcquireAsync(input.StudentUid,input.TermId);
        var target=input.Remove?null:await elements.GetTargetAsync(input.StudentUid,input.TermId);
        if(!input.Remove&&(target is null||target.StudentUid!=input.StudentUid||target.TermId!=input.TermId||target.StatusId!=input.StatusId||input.StatusId<=0))throw new UserFriendlyException("The reviewed student term changed. Review it again.");
        using(var change=units.Begin(requiresNew:true,isTransactional:true))
        {
            var c=await configurations.FindAsync(x=>x.TenantId==owner);
            if(c?.RunningRunId is not null)throw new UserFriendlyException("Wait for the active clearance run to finish before changing exceptions.");
            if(c is null){c=new(GuidGenerator.Create(),owner);await configurations.InsertAsync(c,true);}
            var items=c.Exceptions();var existing=items.SingleOrDefault(x=>x.StudentUid==input.StudentUid&&x.TermId==input.TermId);
            string term;
            if(input.Remove){if(existing is null)throw new UserFriendlyException("This exception was already removed. Refresh the page.");term=existing.Term;items.Remove(existing);}
            else{if(existing is not null)throw new UserFriendlyException("A manual exception already protects this student and term.");if(items.Count>=1000)throw new UserFriendlyException("Review existing exceptions before adding more.");term=target!.Term;items.Add(new(input.StudentUid,input.TermId,term,reason,actor,Clock.Now.ToUniversalTime()));}
            c.SetExceptions(items);await configurations.UpdateAsync(c,true);
            var audit=new ClearanceResetRun(GuidGenerator.Create(),owner);
            audit.SetProperty("ExceptionChangedBy",actor);audit.SetProperty("ExceptionStudentUid",input.StudentUid);audit.SetProperty("ExceptionTermId",input.TermId);audit.SetProperty("ExceptionReason",reason);
            var summary=$"Student UID {input.StudentUid}, {term}: {reason}. No Elements value or bill approval was changed.";
            audit.Finish(input.Remove?"Exception removed":"Exception added",summary.Length>490?summary[..490]+"…":summary);
            await runs.InsertAsync(audit,true);await change.CompleteAsync();
        }
        using var view=units.Begin(requiresNew:true,isTransactional:false);var result=await GetAsync();await view.CompleteAsync();return result;
    }
    [UnitOfWork(IsDisabled=true)]
    public async Task<ClearanceResetSettingsDto> SetAsync(ClearanceResetUpdateDto input)
    {
        var tenant=Tenant();var actor=CurrentUser.Id??throw new UserFriendlyException("Sign in normally to change automatic clearance resets.");
        using(var change=units.Begin(requiresNew:true,isTransactional:true))
        {
            var c=await configurations.FindAsync(x=>x.TenantId==tenant);
            if(input.Enabled)
            {
                elements.EnsureRunEnabled();
                if(c?.RunningRunId is not null)throw new UserFriendlyException("A clearance reset is running or interrupted. It must be checked before enabling.");
                await ReviewBlockers(input.ReviewedRunIds,tenant,actor);
            }
            if(c is null){c=new(GuidGenerator.Create(),tenant);await configurations.InsertAsync(c,true);}
            var state=c.Schedule();state.Configure(input.Enabled,actor,Clock.Now.ToUniversalTime());c.Save(state);await configurations.UpdateAsync(c,true);await change.CompleteAsync();
        }
        using var view=units.Begin(requiresNew:true,isTransactional:false);var result=await GetAsync();await view.CompleteAsync();return result;
    }
    private async Task ReviewBlockers(List<Guid>? reviewedIds,Guid tenant,Guid actor)
    {
        var blockers=await ClearanceResetRunner.Blockers(runs,tenant);
        if(blockers.Any(x=>x.State=="Running"))throw new UserFriendlyException("An unfinished reset requires administrator reconciliation.");
        var reviewed=reviewedIds?.ToHashSet()??[];
        if(blockers.Count>0&&!reviewed.SetEquals(blockers.Select(x=>x.Id)))throw new UserFriendlyException("Check the listed attempts in Elements and confirm no update is still in progress before retrying.");
        foreach(var run in blockers){run.Review(actor,Clock.Now.ToUniversalTime());await runs.UpdateAsync(run,true);}
    }
    [DisableAuditing]
    public async Task<ClearanceResetPreviewDto> PreviewAsync(int? studentUid=null)
    {
        var tenant=Tenant();var actor=CurrentUser.Id??throw new UserFriendlyException("Sign in normally before previewing resets.");
        if(studentUid is <=0)throw new UserFriendlyException("Enter a positive Elements UID or leave it blank.");
        var candidates=(await elements.FindCandidatesAsync()).Where(x=>!studentUid.HasValue||x.StudentUid==studentUid.Value).ToList();
        bool ready;try{elements.EnsureRunEnabled();ready=true;}catch(InvalidOperationException){ready=false;}
        Guid? id=null;var message="No eligible clearance resets were found. Preview does not change Elements.";
        if(candidates.Count>100)message="More than 100 candidates. Enter a Student UID to narrow this manual preview. Automatic runs handle up to 100 per cycle.";
        else if(candidates.Count>0&&!ready)message="Preview only: clearance execution is disabled for this environment.";
        else if(candidates.Count>0)
        {var preview=new ClearanceResetRun(GuidGenerator.Create(),tenant);preview.MakePreview(candidates,actor,Clock.Now.ToUniversalTime());await runs.InsertAsync(preview,true);id=preview.Id;message=$"{candidates.Count} resets ready. Review before Run now. This preview expires in 15 minutes.";}
        return new(id,candidates.Select(x=>new ClearanceResetPreviewRowDto(x.StudentUid,x.StudentId,x.Name,x.TermId,x.Term,"Cleared → blank")).ToList(),message);
    }
    [DisableAuditing]
    [UnitOfWork(IsDisabled=true)]
    public async Task<List<ClearanceResetResultDto>> RunAsync(ClearanceResetRunRequestDto input)
    {
        elements.EnsureRunEnabled();var tenant=Tenant();var actor=CurrentUser.Id??throw new UserFriendlyException("Sign in normally before running resets.");
        List<ClearanceResetCandidate> candidates;
        using(var claim=units.Begin(requiresNew:true,isTransactional:true))
        {
            var preview=await runs.GetAsync(input.RunId);var c=await configurations.FindAsync(x=>x.TenantId==tenant);
            if(preview.PolicyVersion!=2||preview.TenantId!=tenant||preview.PreviewOwner!=actor||preview.State!="Preview"||preview.PreviewExpiresAtUtc is null||Clock.Now.ToUniversalTime()>=preview.PreviewExpiresAtUtc||c?.RunningRunId is not null)
                throw new UserFriendlyException("This preview expired, was already used, belongs to another staff member, or a run is in progress. Create a fresh preview.");
            candidates=preview.PreviewCandidates();if(candidates.Count is <1 or >100)throw new UserFriendlyException("Create a new preview with 1–100 candidates.");
            await ReviewBlockers(input.ReviewedRunIds,tenant,actor);
            if(c is null){c=new(GuidGenerator.Create(),tenant);await configurations.InsertAsync(c,true);}
            preview.StartPreview();await runs.UpdateAsync(preview,true);c.SetRun(preview.Id);var schedule=c.Schedule();schedule.LastStartedAtUtc=Clock.Now.ToUniversalTime();schedule.LastOutcome="Manual run in progress";c.Save(schedule);await configurations.UpdateAsync(c,true);await claim.CompleteAsync();
        }
        return (await runner.ExecuteClaimedAsync(tenant,input.RunId,candidates,false)).Select(x=>new ClearanceResetResultDto(x.StudentUid,x.TermId,x.Term,x.Outcome)).ToList();
    }

}
