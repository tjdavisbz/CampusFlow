using System;
using System.Text.Json;
using Volo.Abp.Data;

namespace CampusFlow.CostTypes;

public sealed class CostTypeAutomationState
{
    public const int IntervalMinutes = 15;
    public const int BatchLimit = 100;
    public bool Enabled { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastFinishedAtUtc { get; set; }
    public Guid? LastRunId { get; set; }
    public string LastOutcome { get; set; } = "Automatic updates have not run.";
    public Guid? ChangedBy { get; set; }
    public DateTime? ChangedAtUtc { get; set; }
    public static CostTypeAutomationState Read(CostTypeConfiguration configuration) =>
        JsonSerializer.Deserialize<CostTypeAutomationState>(configuration.GetProperty<string>("CostTypeAutomationV1") ?? "{}")
        ?? throw new InvalidOperationException("The automatic schedule could not be read.");
    public void Save(CostTypeConfiguration configuration)
    { configuration.EnsureRulesRevision(); configuration.SetProperty("CostTypeAutomationV1",JsonSerializer.Serialize(this)); }
    public bool IsDue(DateTime now) => Enabled && NextRunAtUtc.HasValue && NextRunAtUtc<=now;
    public void Configure(bool enabled,Guid actor,DateTime now)
    {
        if(actor==Guid.Empty) throw new InvalidOperationException("A signed-in administrator is required.");
        if(enabled && !Enabled) NextRunAtUtc=now.AddMinutes(IntervalMinutes);
        if(!enabled) NextRunAtUtc=null;
        Enabled=enabled;ChangedBy=actor;ChangedAtUtc=now;
    }
    public void Started(Guid runId,DateTime now)
    { LastRunId=runId;LastStartedAtUtc=now;NextRunAtUtc=now.AddMinutes(IntervalMinutes);LastOutcome="Running"; }
    public void Finished(DateTime now,string summary,bool needsReview)
    {
        LastFinishedAtUtc=now;LastOutcome=summary;
        if(needsReview) Pause("Paused: an unconfirmed update needs review in Elements.");
        else if(Enabled && (!NextRunAtUtc.HasValue || NextRunAtUtc<=now)) NextRunAtUtc=now.AddMinutes(IntervalMinutes);
    }
    public void Pause(string reason) { Enabled=false;NextRunAtUtc=null;LastOutcome=reason; }
}
