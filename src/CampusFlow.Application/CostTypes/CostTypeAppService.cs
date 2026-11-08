using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
namespace CampusFlow.CostTypes;
[Authorize(CampusFlowPermissions.Admin.CostTypes)]
public class CostTypeAppService(IRepository<CostTypeConfiguration,Guid> configurations,
    IRepository<CostTypeRun,Guid> runs, ICostTypeElements elements, IUnitOfWorkManager uow,CostTypeRunExecutor? executor=null) : CampusFlowAppService, ICostTypeAppService
{
    private Guid Tenant() => CurrentTenant.Id ?? throw new UserFriendlyException("Select a university first.");
    private Task<CostTypeConfiguration?> ConfigurationAsync() { var tenant=Tenant(); return configurations.FindAsync(x=>x.TenantId==tenant); }
    private static List<CostTypeRule> Rules(CostTypeConfiguration c) => JsonSerializer.Deserialize<List<CostTypeRule>>(c.RulesJson)!;
    private static CostTypeRule Map(CostTypeRuleDto x) => new(x.Priority,x.Campus,x.Attendance,x.DegreeMatch,x.Degree,x.CostType,x.CostTypeId,x.Enabled);
    [DisableAuditing]
    public async Task<List<CostTypeHistoryDto>> GetHistoryAsync()
    {
        var tenant=Tenant();
        var query=await runs.GetQueryableAsync();
        var recent=await AsyncExecuter.ToListAsync(query.Where(x=>x.TenantId==tenant && x.State!="Preview")
            .OrderByDescending(x=>x.CreationTime).Take(10));
        return recent.Select(x=>new CostTypeHistoryDto(x.Id,x.CreationTime,x.State,
            JsonSerializer.Deserialize<List<CostTypeResultDto>>(x.ResultsJson)??[],x.IsAutomatic,x.Summary,x.IsReviewed)).ToList();
    }
    public async Task<CostTypeSetupDto> GetAsync()
    {
        var c=await ConfigurationAsync();
        List<CostTypeRuleDto> rules;
        if(c is null)
        {
            using var stream=typeof(CostTypeAppService).Assembly.GetManifestResourceStream("CampusFlow.CostTypes.CamsCostTypeBaseline.json")!;
            rules=(await JsonSerializer.DeserializeAsync<List<CostTypeRuleDto>>(stream))!;
        }
        else rules=JsonSerializer.Deserialize<List<CostTypeRuleDto>>(c.RulesJson)!;
        var choices=await elements.GetChoicesAsync();
        foreach(var rule in rules.Where(x=>x.CostTypeId is null))
        {
            var matches=choices.Where(x=>string.Equals(x.Label.TrimEnd(),rule.CostType.TrimEnd(),StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count==1) { rule.CostTypeId=matches[0].Id;rule.CostType=matches[0].Label; }
        }
        bool canRun; try { elements.EnsureRunEnabled(); canRun=true; } catch(InvalidOperationException) { canRun=false; }
        return new() { Revision=c?.RulesRevision??"",Rules=rules,CanRun=canRun,
            Choices=choices.Select(x=>new CostTypeChoiceDto(x.Id,x.Label,x.Description)).ToList(),Automatic=await AutomaticAsync(c) };
    }
    public async Task<CostTypeSetupDto> SaveAsync(CostTypeSetupDto input)
    {
        var tenant=Tenant();
        if(input.Rules is null || input.Rules.Count is <1 or >1000 || string.IsNullOrWhiteSpace(input.ChangeReason) || input.ChangeReason.Length>500)
            throw new UserFriendlyException("Enter 1–1,000 rules and a change reason (up to 500 characters).");
        var choices=await elements.GetChoicesAsync(); var priorities=new HashSet<int>();
        foreach(var r in input.Rules)
        {
            if(r.Priority<=0 || !priorities.Add(r.Priority) || string.IsNullOrWhiteSpace(r.Attendance) || r.Attendance.Length>160 ||
               r.Campus?.Length>160 || r.Degree?.Length>200 || r.DegreeMatch is not ("ANY" or "NULL" or "EXACT") ||
               (r.DegreeMatch=="EXACT" && r.Degree is null))
                throw new UserFriendlyException("Check rule priorities, attendance and degree conditions. Priorities must be unique.");
            // Unresolved legacy mappings may be saved for review. They remain Invalid lookup
            // in preview and can never become executable candidates or fall through to a later rule.
            if(r.CostType is null || r.CostType.Length>100) throw new UserFriendlyException($"Cost type is too long for rule {r.Priority}.");
            if(r.Enabled && r.CostTypeId is not null)
            {
                var matches=choices.Where(x=>x.Id==r.CostTypeId && string.Equals(x.Label.Trim(),r.CostType.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count!=1)throw new UserFriendlyException($"Choose an active Elements cost type for rule {r.Priority}.");
                r.CostType=matches[0].Label;
            }
        }
        var c=await ConfigurationAsync();
        if((c?.RulesRevision??"")!=input.Revision || c?.RunningRunId is not null)
            throw new UserFriendlyException("Rules changed or a run is in progress. Reload before saving.");
        var json=JsonSerializer.Serialize(input.Rules.OrderBy(x=>x.Priority).Select(Map));
        if(c is null) await configurations.InsertAsync(new CostTypeConfiguration(GuidGenerator.Create(),tenant,json,input.ChangeReason),true);
        else { c.Edit(json,input.ChangeReason); await configurations.UpdateAsync(c,true); }
        return await GetAsync();
    }
    [DisableAuditing]
    public async Task<CostTypePreviewDto> PreviewAsync(int? studentUid=null)
    {
        var c=await ConfigurationAsync() ?? throw new UserFriendlyException("Review and save the baseline rules before previewing.");
        if(studentUid<=0) throw new UserFriendlyException("Enter a positive student UID or leave it blank.");
        var rows=await elements.PreviewAsync(Rules(c),studentUid);
        var changes=rows.Where(x=>x.Action=="Would change").ToList();
        var candidates=new List<CostTypeCandidate>();
        var message="Preview only. Unmapped and invalid rows will be skipped.";
        bool enabled; try { elements.EnsureRunEnabled(); enabled=true; } catch(InvalidOperationException) { enabled=false; }
        Guid? id=null;
        if(enabled && changes.Count is >0 and <=100)
        {
            foreach(var row in changes) candidates.Add(new(row,await elements.FingerprintAsync(row)));
            var run=new CostTypeRun(GuidGenerator.Create(),Tenant(),c.RulesRevision,c.RulesJson,JsonSerializer.Serialize(candidates),studentUid);
            await runs.InsertAsync(run,true); id=run.Id;
            message=$"{changes.Count} changes ready. Preview expires in 15 minutes. Unmapped and invalid rows will be skipped.";
        }
        else if(changes.Count>100) message="More than 100 changes. Preview one student at a time for this validation phase.";
        else if(!enabled) message="Preview only. Execution is not enabled for this environment.";
        return new(id,rows.Select(x=>new CostTypeRowDto(x.StudentUid,x.StudentId,x.Name,x.StatusId,x.TermId,x.Term,x.Campus,x.Attendance,x.Degree,x.CurrentId,x.CurrentLabel,x.Rule,x.ProposedId,x.ProposedLabel,x.Action)).ToList(),message);
    }
    [DisableAuditing]
    [UnitOfWork(IsDisabled=true)]
    public async Task<List<CostTypeResultDto>> RunAsync(Guid runId)
    {
        elements.EnsureRunEnabled(); var tenant=Tenant(); List<CostTypeCandidate> candidates; List<CostTypeRule> rules; int? studentUid;
        using(var claim=uow.Begin(requiresNew:true,isTransactional:true))
        {
            var run=await runs.GetAsync(runId); var c=await ConfigurationAsync() ?? throw new UserFriendlyException("Rules are missing.");
            if(run.TenantId!=tenant || run.CreatorId!=CurrentUser.Id || run.State!="Preview" ||
               Clock.Now-run.CreationTime>TimeSpan.FromMinutes(15) || run.RuleRevision!=c.RulesRevision || c.RunningRunId is not null)
                throw new UserFriendlyException("This preview expired, changed, or has already been used. Create a new preview.");
            candidates=JsonSerializer.Deserialize<List<CostTypeCandidate>>(run.CandidatesJson)!; rules=Rules(c); studentUid=run.StudentUid;
            run.Start(); c.SetRun(run.Id); await runs.UpdateAsync(run,true); await configurations.UpdateAsync(c,true); await claim.CompleteAsync();
        }
        return await (executor ?? new CostTypeRunExecutor(configurations,runs,elements,uow))
            .ExecuteAsync(runId,tenant,candidates,rules,studentUid,false);
    }
    private async Task<CostTypeAutomaticSettingsDto> AutomaticAsync(CostTypeConfiguration? c)
    {
        if(c is null)return new();
        var state=CostTypeAutomationState.Read(c);
        var blockers=await CostTypeRunExecutor.BlockingRunsAsync(runs,Tenant());
        return new() {Enabled=state.Enabled,NextRunAtUtc=state.NextRunAtUtc,LastStartedAtUtc=state.LastStartedAtUtc,
            LastFinishedAtUtc=state.LastFinishedAtUtc,LastRunId=state.LastRunId,RunningRunId=c.RunningRunId,
            LastOutcome=state.LastOutcome,ReviewRuns=blockers.Select(x=>new CostTypeReviewRunDto(x.Id,x.CreationTime,x.State)).ToList()};
    }
    [DisableAuditing]
    public async Task<CostTypeAutomaticSettingsDto> GetAutomaticRunsAsync() => await AutomaticAsync(await ConfigurationAsync());
    [UnitOfWork(IsDisabled=true)]
    public async Task<CostTypeAutomaticSettingsDto> SetAutomaticRunsAsync(CostTypeAutomaticUpdateDto input)
    {
        var tenant=Tenant();var actor=CurrentUser.Id ?? throw new UserFriendlyException("Sign in normally to change the automatic schedule.");
        using(var change=uow.Begin(requiresNew:true,isTransactional:true))
        {
            var c=await ConfigurationAsync() ?? throw new UserFriendlyException("Review and save the rules before enabling automatic updates.");
            if(input.Enabled)
            {
                elements.EnsureRunEnabled();
                if(c.RulesRevision!=input.Revision)throw new UserFriendlyException("Saved rules changed. Reload and review them before enabling automatic updates.");
                if(c.RunningRunId is not null)throw new UserFriendlyException("A run is still in progress or interrupted. Check it before enabling automatic updates.");
                if(!Rules(c).Any(x=>x.Enabled && x.CostTypeId is not null))throw new UserFriendlyException("Save at least one mapped, enabled rule first.");
                var blockers=await CostTypeRunExecutor.BlockingRunsAsync(runs,tenant);
                if(blockers.Any(x=>x.State=="Running"))throw new UserFriendlyException("An unfinished run still needs administrator review. It cannot be retried automatically.");
                var reviewed=input.ReviewedRunIds?.ToHashSet() ?? [];
                if(blockers.Count>0 && !reviewed.SetEquals(blockers.Select(x=>x.Id)))
                    throw new UserFriendlyException("Check the listed runs in Elements and confirm that no request is still in progress before enabling automatic updates.");
                foreach(var run in blockers){run.Review(actor,Clock.Now.ToUniversalTime());await runs.UpdateAsync(run,true);}
            }
            // Turning off is always local: it does not depend on Elements availability or discard unsaved rule edits.
            var schedule=CostTypeAutomationState.Read(c);schedule.Configure(input.Enabled,actor,Clock.Now.ToUniversalTime());schedule.Save(c);
            await configurations.UpdateAsync(c,true);await change.CompleteAsync();
        }
        using var view=uow.Begin(requiresNew:true,isTransactional:false);
        var result=await AutomaticAsync(await ConfigurationAsync());await view.CompleteAsync();return result;
    }
}
