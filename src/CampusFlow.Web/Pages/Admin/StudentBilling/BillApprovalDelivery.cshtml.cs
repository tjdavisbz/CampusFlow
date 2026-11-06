using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Admin.StudentBilling;

[Authorize(CampusFlowPermissions.Admin.BillApproval)]
public class BillApprovalDeliveryModel(IRepository<BillApproval,Guid> approvals,
    IRepository<BillApprovalArtifact,Guid> artifacts, IBackgroundJobManager jobs, Volo.Abp.Linq.IAsyncQueryableExecuter executer) : CampusFlowPageModel
{
    [BindProperty(SupportsGet=true)] public string? StudentId {get;set;}
    [TempData] public string? Notice {get;set;}
    public List<DeliveryRow> Rows {get;private set;}=[];
    public async Task OnGetAsync()
    {
        var query=await approvals.GetQueryableAsync();
        var rows=await executer.ToListAsync(query.Where(x=>x.TenantId==CurrentTenant.Id && x.AcceptedAt!=null &&
            (x.Status==BillApprovalStatus.Approved||x.Status==BillApprovalStatus.DocumentPending||x.Status==BillApprovalStatus.Completed) &&
            (StudentId==null||StudentId==""||x.StudentId==StudentId)).OrderByDescending(x=>x.AcceptedAt).Take(50));
        Rows=rows.Select(x=>new DeliveryRow(x.Id,x.StudentId,x.TermName,x.AcceptedAt!.Value,BillApprovalCompletionProgress.Read(x))).ToList();
    }
    private Task<BillApproval?> Find(Guid id)=>approvals.FindAsync(x=>x.Id==id && x.TenantId==CurrentTenant.Id && x.AcceptedAt!=null &&
        (x.Status==BillApprovalStatus.Approved||x.Status==BillApprovalStatus.DocumentPending||x.Status==BillApprovalStatus.Completed));
    public async Task<IActionResult> OnPostRetryAsync(Guid id,bool confirmedUnsent=false)
    {
        if(CurrentTenant.Id is null)return NotFound();var approval=await Find(id);if(approval is null)return NotFound();
        var p=BillApprovalCompletionProgress.Read(approval);
        try{p.ResetForRetry(DateTime.UtcNow,confirmedUnsent);}
        catch(InvalidOperationException ex){Notice=ex.Message;return RedirectToPage(new{StudentId});}
        if(confirmedUnsent)foreach(var step in Enum.GetValues<BillCompletionStep>().Where(BillApprovalCompletionProgress.IsEmail))
        {p.Step(step).ReconciledBy=CurrentUser.Id;p.Step(step).ReconciledAt=DateTime.UtcNow;}
        p.Save(approval);await approvals.UpdateAsync(approval,autoSave:true);
        await jobs.EnqueueAsync(new BillApprovalCompletionJobArgs(CurrentTenant.Id.Value,id));
        Notice="Pending work is queued. Uncertain emails remain blocked unless confirmed unsent; completed deliveries are preserved.";
        return RedirectToPage(new{StudentId});
    }
    public async Task<IActionResult> OnPostConfirmSentAsync(Guid id,BillCompletionStep step,bool confirmedSent=false)
    {
        if(CurrentTenant.Id is null||!confirmedSent||!BillApprovalCompletionProgress.IsEmail(step))return BadRequest();
        var approval=await Find(id);if(approval is null)return NotFound();var p=BillApprovalCompletionProgress.Read(approval);
        if(p.Step(step).Status!=BillCompletionStatus.Uncertain)return BadRequest();
        p.Finish(step,BillCompletionStatus.Completed);p.Step(step).ReconciledBy=CurrentUser.Id;p.Step(step).ReconciledAt=DateTime.UtcNow;p.Save(approval);
        await approvals.UpdateAsync(approval,autoSave:true);
        var artifact=await artifacts.FindAsync(x=>x.BillApprovalId==id && x.TenantId==CurrentTenant.Id);
        if(artifact is not null){artifact.MarkEmailAccepted(step==BillCompletionStep.BookstoreEmail,DateTime.UtcNow);await artifacts.UpdateAsync(artifact,autoSave:true);}
        await jobs.EnqueueAsync(new BillApprovalCompletionJobArgs(CurrentTenant.Id.Value,id));
        Notice="The email was recorded as confirmed sent. It will not be sent again.";return RedirectToPage(new{StudentId});
    }
    public static string StepName(BillCompletionStep step)=>step switch
    {BillCompletionStep.Pdf=>"Approved PDF",BillCompletionStep.Cleared=>"Elements Cleared status",BillCompletionStep.Document=>"Billing document tracking",BillCompletionStep.BookstoreEmail=>"Bookstore email",_=>"Student email and PDF"};
    public static string StatusName(BillCompletionStatus status)=>status switch
    {BillCompletionStatus.Completed=>"Confirmed",BillCompletionStatus.Uncertain=>"Needs delivery review",BillCompletionStatus.Disabled=>"Not enabled",BillCompletionStatus.Running=>"Processing",BillCompletionStatus.Failed=>"Needs retry",_=>"Waiting"};
    public sealed record DeliveryRow(Guid Id,string StudentId,string Term,DateTime ApprovedAt,BillApprovalCompletionProgress Progress);
}
