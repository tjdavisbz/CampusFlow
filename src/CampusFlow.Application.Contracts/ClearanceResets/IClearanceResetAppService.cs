using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
namespace CampusFlow.ClearanceResets;
public record ClearanceResetRunDto(Guid Id,DateTime Created,string State,string Summary,bool Reviewed,List<ClearanceResetResultDto> Results,bool Automatic=false);
public record ClearanceResetResultDto(int StudentUid,int TermId,string Term,string Outcome);
public class ClearanceResetSettingsDto
{
    public bool Enabled {get;set;}
    public bool CanEnable {get;set;}
    public Guid? RunningRunId {get;set;}
    public DateTime? NextRunAtUtc {get;set;}
    public DateTime? LastStartedAtUtc {get;set;}
    public DateTime? LastFinishedAtUtc {get;set;}
    public string LastOutcome {get;set;}="Automatic clearance resets have not run.";
    public List<ClearanceResetRunDto> RecentRuns {get;set;}=[];
    public List<ClearanceResetRunDto> ReviewRuns {get;set;}=[];
}
public class ClearanceResetUpdateDto
{public bool Enabled {get;set;} public List<Guid> ReviewedRunIds {get;set;}=[];}
public record ClearanceResetPreviewRowDto(int StudentUid,string StudentId,string Name,int TermId,string Term,string Action);
public record ClearanceResetPreviewDto(Guid? RunId,List<ClearanceResetPreviewRowDto> Rows,string Message);
public class ClearanceResetRunRequestDto
{public Guid RunId {get;set;} public List<Guid> ReviewedRunIds {get;set;}=[];}
public interface IClearanceResetAppService:IApplicationService
{
    Task<ClearanceResetSettingsDto> GetAsync();
    Task<ClearanceResetPreviewDto> PreviewAsync(int? studentUid=null);
    Task<List<ClearanceResetResultDto>> RunAsync(ClearanceResetRunRequestDto input);
    Task<ClearanceResetSettingsDto> SetAsync(ClearanceResetUpdateDto input);
}
