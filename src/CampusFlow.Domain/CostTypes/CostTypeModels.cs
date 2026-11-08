using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using Volo.Abp.Data;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.CostTypes;
public sealed record CostTypeRule(int Priority, string? Campus, string Attendance, string DegreeMatch,
    string? Degree, string CostType, int? CostTypeId, bool Enabled = true);
public sealed record CostTypeChoice(int Id, string Label, string Description);
public sealed record CostTypePreviewRow(int StudentUid, string StudentId, string Name, int StatusId,
    int TermId, string Term, string? Campus, string? Attendance, string? Degree,
    int CurrentId, string CurrentLabel, int? Rule, int? ProposedId, string? ProposedLabel, string Action);
public sealed record CostTypeCandidate(CostTypePreviewRow Row, string ApiFingerprint);
public interface ICostTypeElements
{
    void EnsureRunEnabled();
    Task<List<CostTypeChoice>> GetChoicesAsync(CancellationToken cancellationToken = default);
    Task<List<CostTypePreviewRow>> PreviewAsync(List<CostTypeRule> rules, int? studentUid, CancellationToken cancellationToken = default);
    Task<string> FingerprintAsync(CostTypePreviewRow row, CancellationToken cancellationToken = default);
    Task ApplyAsync(CostTypeCandidate candidate, CancellationToken cancellationToken = default);
}
public class CostTypeConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string RulesJson { get; private set; } = "[]";
    public string ChangeReason { get; private set; } = "";
    public Guid? RunningRunId { get; private set; }
    protected CostTypeConfiguration() { }
    public CostTypeConfiguration(Guid id, Guid tenantId, string json, string reason) : base(id)
    { TenantId=tenantId; RulesJson=json; ChangeReason=reason; }
    [NotMapped] public string RulesRevision => this.GetProperty<string>("CostTypeRulesRevision") ?? ConcurrencyStamp;
    public void EnsureRulesRevision() => this.SetProperty("CostTypeRulesRevision", RulesRevision);
    public void Edit(string json,string reason)
    { RulesJson=json; ChangeReason=reason; this.SetProperty("CostTypeRulesRevision",Guid.NewGuid().ToString("N")); }
    public void SetRun(Guid? id) { EnsureRulesRevision(); RunningRunId=id; }
}
public class CostTypeRun : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string RuleRevision { get; private set; } = "";
    public string RulesSnapshotJson { get; private set; } = "[]";
    public string CandidatesJson { get; private set; } = "[]";
    public string ResultsJson { get; private set; } = "[]";
    public string State { get; private set; } = "Preview";
    public int? StudentUid { get; private set; }
    protected CostTypeRun() { }
    public CostTypeRun(Guid id,Guid tenantId,string revision,string rules,string candidates,int? studentUid):base(id)
    { TenantId=tenantId;RuleRevision=revision;RulesSnapshotJson=rules;CandidatesJson=candidates;StudentUid=studentUid; }
    [NotMapped] public bool IsAutomatic => this.GetProperty<bool>("CostTypeAutomaticRun");
    [NotMapped] public bool IsReviewed => this.GetProperty<Guid?>("CostTypeReviewedBy").HasValue;
    [NotMapped] public string Summary => this.GetProperty<string>("CostTypeSummary") ?? "";
    public void MarkAutomatic() => this.SetProperty("CostTypeAutomaticRun",true);
    public void Prepare(string rules,string candidates) { RulesSnapshotJson=rules; CandidatesJson=candidates; }
    public void SetSummary(string summary) => this.SetProperty("CostTypeSummary",summary);
    public void Review(Guid actor,DateTime at)
    {
        if(State is "Running" or "Preview" or "Completed" or "Completed with skips" or "Failed before changes" or "Stopped" || actor==Guid.Empty) throw new InvalidOperationException("Only an unconfirmed completed run can be reviewed.");
        this.SetProperty("CostTypeReviewedBy",actor);this.SetProperty("CostTypeReviewedAt",at);
    }
    public void Start() => State="Running";
    public void Record(string results) => ResultsJson=results;
    public void Finish(string state) => State=state;
}
