using System;
using System.Collections.Generic;
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
    Task<List<CostTypeChoice>> GetChoicesAsync();
    Task<List<CostTypePreviewRow>> PreviewAsync(List<CostTypeRule> rules, int? studentUid);
    Task<string> FingerprintAsync(CostTypePreviewRow row);
    Task ApplyAsync(CostTypeCandidate candidate);
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
    public void Edit(string json,string reason) { RulesJson=json; ChangeReason=reason; }
    public void SetRun(Guid? id) => RunningRunId=id;
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
    public void Start() => State="Running";
    public void Record(string results) => ResultsJson=results;
    public void Finish(string state) => State=state;
}
