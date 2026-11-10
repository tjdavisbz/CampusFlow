using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.ClearanceResets;

public record ClearanceResetCandidate(int StudentUid,int TermId,int StatusId,string Term,string TermSequence,int TransferCategoryId=0,string StudentId="",string Name="");
public record ClearanceResetAttempt(ClearanceResetCandidate Candidate,string BeforeJson,string DesiredJson);
public record ClearanceResetResult(int StudentUid,int TermId,string Term,string Outcome);
public interface IClearanceResetElements
{
    void EnsureRunEnabled();
    Task<List<ClearanceResetTerm>> GetTermsAsync(CancellationToken cancellationToken=default);
    Task<ClearanceResetCandidate> GetTargetAsync(int studentUid,int termId,CancellationToken cancellationToken=default);
    Task<List<ClearanceResetCandidate>> FindCandidatesAsync(CancellationToken cancellationToken=default);
    Task<ClearanceResetResult> ResetIfEligibleAsync(ClearanceResetCandidate candidate,
        Func<ClearanceResetAttempt,Task<bool>> recordBeforeWrite,CancellationToken cancellationToken=default);
}
public sealed class ClearanceResetUnconfirmedException : Exception
{public ClearanceResetUnconfirmedException(Exception inner):base("An Elements update could not be confirmed. Check the recorded student and term before retrying.",inner) {}}
public sealed class ClearanceResetConfiguration : FullAuditedAggregateRoot<Guid>,IMultiTenant
{
    public Guid? TenantId {get;private set;}
    public Guid? RunningRunId {get;private set;}
    private ClearanceResetConfiguration() { }
    public ClearanceResetConfiguration(Guid id,Guid tenant):base(id){TenantId=tenant;}
    public void SetRun(Guid? id)=>RunningRunId=id;
    public List<ManualClearanceException> Exceptions()=>JsonSerializer.Deserialize<List<ManualClearanceException>>(this.GetProperty<string>("ManualClearanceExceptionsV1")??"[]")??throw new InvalidOperationException("Manual exceptions are unavailable.");
    public void SetExceptions(List<ManualClearanceException> items)=>this.SetProperty("ManualClearanceExceptionsV1",JsonSerializer.Serialize(items));
    public ClearanceResetSchedule Schedule()=>JsonSerializer.Deserialize<ClearanceResetSchedule>(this.GetProperty<string>("ScheduleV1")??"{}")!;
    public void Save(ClearanceResetSchedule state)=>this.SetProperty("ScheduleV1",JsonSerializer.Serialize(state));
}
public sealed class ClearanceResetSchedule
{
    public const int IntervalMinutes=15;
    public const int BatchLimit=100;
    public int PolicyVersion {get;set;}
    public bool Enabled {get;set;}
    public DateTime? NextRunAtUtc {get;set;}
    public DateTime? LastStartedAtUtc {get;set;}
    public DateTime? LastFinishedAtUtc {get;set;}
    public string LastOutcome {get;set;}="Automatic clearance resets have not run.";
    public Guid? ChangedBy {get;set;}
    public DateTime? ChangedAtUtc {get;set;}
    public bool IsDue(DateTime now)=>PolicyVersion==2&&Enabled&&NextRunAtUtc.HasValue&&NextRunAtUtc<=now;
    public void Configure(bool enabled,Guid actor,DateTime now)
    {
        if(actor==Guid.Empty)throw new InvalidOperationException("A signed-in staff member is required.");
        if(enabled&&(!Enabled||PolicyVersion!=2))NextRunAtUtc=now.AddMinutes(IntervalMinutes);
        if(enabled)PolicyVersion=2;
        if(!enabled)NextRunAtUtc=null;
        Enabled=enabled;ChangedBy=actor;ChangedAtUtc=now;
    }
    public void Started(DateTime now){LastStartedAtUtc=now;NextRunAtUtc=now.AddMinutes(IntervalMinutes);LastOutcome="Running";}
    public void Finished(DateTime now,string summary,bool uncertain)
    {LastFinishedAtUtc=now;LastOutcome=summary;if(uncertain)Pause("Paused: an unconfirmed clearance reset needs review in Elements.");else if(Enabled&&NextRunAtUtc<=now)NextRunAtUtc=now.AddMinutes(IntervalMinutes);}
    public void Pause(string reason){Enabled=false;NextRunAtUtc=null;LastOutcome=reason;}
}
public sealed class ClearanceResetRun : FullAuditedAggregateRoot<Guid>,IMultiTenant
{
    public Guid? TenantId {get;private set;}
    public string State {get;private set;}="Running";
    public string ResultsJson {get;private set;}="[]";
    public string AttemptsJson {get;private set;}="[]";
    public string Summary {get;private set;}="Running";
    [NotMapped] public bool Automatic=>this.GetProperty<bool>("Automatic");
    [NotMapped] public Guid? PreviewOwner=>this.GetProperty<Guid?>("PreviewOwner");
    [NotMapped] public DateTime? PreviewExpiresAtUtc=>this.GetProperty<DateTime?>("PreviewExpiresAtUtc");
    public void MarkAutomatic()=>this.SetProperty("Automatic",true);
    public void MakePreview(List<ClearanceResetCandidate> candidates,Guid actor,DateTime now)
    {if(actor==Guid.Empty)throw new InvalidOperationException("A signed-in staff member is required.");State="Preview";Summary="Preview only";this.SetProperty("PreviewOwner",actor);this.SetProperty("PreviewExpiresAtUtc",now.AddMinutes(15));this.SetProperty("PreviewCandidates",JsonSerializer.Serialize(candidates));this.SetProperty("ClearancePolicyVersion",2);}
    public List<ClearanceResetCandidate> PreviewCandidates()=>JsonSerializer.Deserialize<List<ClearanceResetCandidate>>(this.GetProperty<string>("PreviewCandidates")??"[]")!;
    [NotMapped] public int PolicyVersion=>this.GetProperty<int>("ClearancePolicyVersion");
    public void StartPreview(){if(State!="Preview")throw new InvalidOperationException("Preview already used.");State="Running";Summary="Manual run in progress";}
    [NotMapped] public bool Reviewed=>this.GetProperty<Guid?>("ReviewedBy").HasValue;
    private ClearanceResetRun() { }
    public ClearanceResetRun(Guid id,Guid tenant):base(id){TenantId=tenant;}
    public void RecordResults(List<ClearanceResetResult> results)=>ResultsJson=JsonSerializer.Serialize(results);
    public void RecordAttempt(ClearanceResetAttempt attempt)
    {var list=JsonSerializer.Deserialize<List<ClearanceResetAttempt>>(AttemptsJson)!;list.Add(attempt);AttemptsJson=JsonSerializer.Serialize(list);}
    public void Finish(string state,string summary){State=state;Summary=summary;}
    public void Review(Guid actor,DateTime now)
    {if(State!="Needs review"||actor==Guid.Empty)throw new InvalidOperationException("Only an unconfirmed finished run can be reviewed.");this.SetProperty("ReviewedBy",actor);this.SetProperty("ReviewedAtUtc",now);}
}
