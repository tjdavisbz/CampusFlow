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
    public List<ManualClearanceExceptionDto> ManualExceptions {get;set;}=[];
    public List<ClearanceResetRunDto> RecentRuns {get;set;}=[];
    public List<ClearanceResetRunDto> ReviewRuns {get;set;}=[];
}
public class ClearanceResetUpdateDto
{public bool Enabled {get;set;} public List<Guid> ReviewedRunIds {get;set;}=[];}
public record ClearanceResetPreviewRowDto(int StudentUid,string StudentId,string Name,int TermId,string Term,string Action);
public record ClearanceResetPreviewDto(Guid? RunId,List<ClearanceResetPreviewRowDto> Rows,string Message);
public class ClearanceResetRunRequestDto
{public Guid RunId {get;set;} public List<Guid> ReviewedRunIds {get;set;}=[];}
public record ManualClearanceTermDto(int TermId,string Term);
public record ManualClearanceExceptionDto(int StudentUid,int TermId,string Term,string Reason,DateTime CreatedAtUtc);
public record ManualClearanceTargetDto(int StudentUid,int TermId,int StatusId,string StudentId,string Name,string Term);
public class ManualClearanceExceptionInput
{public int StudentUid {get;set;} public int TermId {get;set;} public int StatusId {get;set;} public string Reason {get;set;}=""; public bool Confirmed {get;set;} public bool Remove {get;set;}}
public interface IClearanceResetAppService:IApplicationService
{
    Task<ClearanceResetSettingsDto> GetAsync();
    Task<List<ManualClearanceTermDto>> GetExceptionTermsAsync();
    Task<ManualClearanceTargetDto> PreviewExceptionAsync(int studentUid,int termId);
    Task<ClearanceResetSettingsDto> SetExceptionAsync(ManualClearanceExceptionInput input);
    Task<ClearanceResetPreviewDto> PreviewAsync(int? studentUid=null);
    Task<List<ClearanceResetResultDto>> RunAsync(ClearanceResetRunRequestDto input);
    Task<ClearanceResetSettingsDto> SetAsync(ClearanceResetUpdateDto input);
}
