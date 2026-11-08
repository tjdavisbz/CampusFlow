using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
namespace CampusFlow.CostTypes;
public class CostTypeRuleDto
{
    public int Priority { get; set; }
    public string? Campus { get; set; }
    public string Attendance { get; set; } = "";
    public string DegreeMatch { get; set; } = "ANY";
    public string? Degree { get; set; }
    public string CostType { get; set; } = "";
    public int? CostTypeId { get; set; }
    public bool Enabled { get; set; } = true;
}
public record CostTypeChoiceDto(int Id,string Label,string Description);
public class CostTypeSetupDto
{
    public string Revision { get; set; } = "";
    public string ChangeReason { get; set; } = "";
    public bool CanRun { get; set; }
    public List<CostTypeRuleDto> Rules { get; set; } = [];
    public List<CostTypeChoiceDto> Choices { get; set; } = [];
    public CostTypeAutomaticSettingsDto Automatic { get; set; } = new();
}
public record CostTypeRowDto(int StudentUid,string StudentId,string Name,int StatusId,int TermId,
    string Term,string? Campus,string? Attendance,string? Degree,int CurrentId,string CurrentLabel,
    int? Rule,int? ProposedId,string? ProposedLabel,string Action);
public record CostTypePreviewDto(Guid? RunId,List<CostTypeRowDto> Rows,string Message);
public record CostTypeResultDto(int StudentUid,int TermId,string Outcome);
public record CostTypeHistoryDto(Guid Id,DateTime Created,string State,List<CostTypeResultDto> Results,
    bool Automatic = false,string Summary = "",bool Reviewed = false);
public record CostTypeReviewRunDto(Guid Id,DateTime Created,string State);
public class CostTypeAutomaticSettingsDto
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 15;
    public int BatchLimit { get; set; } = 100;
    public DateTime? NextRunAtUtc { get; set; }
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastFinishedAtUtc { get; set; }
    public Guid? LastRunId { get; set; }
    public Guid? RunningRunId { get; set; }
    public string LastOutcome { get; set; } = "Automatic updates have not run.";
    public List<CostTypeReviewRunDto> ReviewRuns { get; set; } = [];
}
public class CostTypeAutomaticUpdateDto
{
    public bool Enabled { get; set; }
    public string Revision { get; set; } = "";
    public List<Guid> ReviewedRunIds { get; set; } = [];
}
public interface ICostTypeAppService : IApplicationService
{
    Task<CostTypeSetupDto> GetAsync();
    Task<CostTypeAutomaticSettingsDto> GetAutomaticRunsAsync();
    Task<CostTypeAutomaticSettingsDto> SetAutomaticRunsAsync(CostTypeAutomaticUpdateDto input);
    Task<List<CostTypeHistoryDto>> GetHistoryAsync();
    Task<CostTypeSetupDto> SaveAsync(CostTypeSetupDto input);
    Task<CostTypePreviewDto> PreviewAsync(int? studentUid = null);
    Task<List<CostTypeResultDto>> RunAsync(Guid runId);
}
