using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Permissions;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Admin.StudentBilling;

[Authorize(CampusFlowPermissions.Admin.BillApproval)]
public class BillApprovalDeliveryModel(IRepository<BillApproval,Guid> approvals,
    IRepository<BillApprovalArtifact,Guid> artifacts, IBackgroundJobManager jobs, Volo.Abp.Linq.IAsyncQueryableExecuter executer, IConfiguration? configuration = null, IBillApprovalMailSender? sender = null,
    IBillApprovalPdfGenerator? pdfGenerator = null, ILogger<BillApprovalDeliveryModel>? logger = null) : CampusFlowPageModel
{
    public bool IsNonProduction => configuration is not null && BillApprovalNotification.IsNonProduction(configuration);
    public bool IsReadOnlyView => HttpContext.User.HasClaim(x => x.Type == StudentViewClaimTypes.ExternalStudentId ||
        x.Type == FacultyViewClaimTypes.FacultyId || x.Type == "CampusFlow.SchedulerView.Email");
    public IReadOnlyList<SetupCheck> SetupChecks => [
        new("Approved PDF", pdfGenerator is not null, "Generated from the saved approval snapshot."),
        new("Elements Cleared status", configuration?.GetValue<bool>("BillApproval:ClearedStatus:Enabled") == true, "Uses the selected student's term record."),
        new("Elements billing document", configuration?.GetValue<bool>("BillApproval:DocumentTracking:Enabled") == true, "Type, status and location must be verified for this environment."),
        new("Azure email", configuration?.GetValue<bool>("BillApproval:Notifications:Enabled") == true && sender?.IsConfigured == true,
            IsNonProduction ? "Both messages go only to the signed-in approval actor, saved for later background delivery. Bcc is removed." : "Uses the configured verified sender and intended student/bookstore recipients.")];
    public sealed record SetupCheck(string Name, bool Configured, string Detail);
    [BindProperty(SupportsGet=true)] public string? StudentId {get;set;}
    [TempData] public string? Notice {get;set;}
    public List<DeliveryRow> Rows {get;private set;}=[];
    public async Task OnGetAsync()
    {
        var query=await approvals.GetQueryableAsync();
        var rows=await executer.ToListAsync(query.Where(x=>x.TenantId==CurrentTenant.Id && x.AcceptedAt!=null &&
            (x.Status==BillApprovalStatus.Approved||x.Status==BillApprovalStatus.DocumentPending||x.Status==BillApprovalStatus.Completed) &&
            (StudentId==null||StudentId==""||x.StudentId==StudentId)).OrderByDescending(x=>x.AcceptedAt).Take(50));
        var ids = rows.Select(x => x.Id).ToArray();
        var storedArtifacts = await artifacts.GetListAsync(x => ids.Contains(x.BillApprovalId));
        Rows=rows.Select(x=>new DeliveryRow(x.Id,x.StudentId,x.TermName,x.AcceptedAt!.Value,BillApprovalCompletionProgress.Read(x),
            !storedArtifacts.Any(a=>a.BillApprovalId==x.Id && !string.IsNullOrWhiteSpace(a.ElementsDocumentTrackingId)),
            IsNonProduction ? BillApprovalTestRecipient.Read(x)?.Email : null)).ToList();
    }
    public async Task<IActionResult> OnPostTestEmailAsync()
    {
        if (!IsNonProduction || IsReadOnlyView || CurrentUser.Id is not Guid actor || CurrentTenant.Id is not Guid tenantId) return Forbid();
        if (configuration is null || sender?.IsConfigured != true || pdfGenerator is null ||
            !configuration.GetValue<bool>("BillApproval:Notifications:Enabled"))
        { Notice="Azure email is not configured/enabled. No message was sent."; return RedirectToPage(new { StudentId }); }
        var operationId=Guid.NewGuid();
        try
        {
            var bill=new BillApproval(Guid.NewGuid(),tenantId,actor,Guid.NewGuid(),"0","CF004-TEST","0","TEST","CF-004 readiness check");
            BillApprovalTestRecipient.Capture(bill,actor,CurrentUser.Email,DateTime.UtcNow);
            bill.UpdateDraft(BillPaymentChoice.NoBalanceDue,0,0,0,0,0,"[]",System.Text.Json.JsonSerializer.Serialize(
                new BillApprovalReviewSnapshot("CampusFlow Test",0,[],[],[])));
            bill.Accept(Guid.NewGuid(),1,"Synthetic CF-004 setup check. This is not a student bill.",DateTime.UtcNow,null,null);
            using var message=BillApprovalNotification.Create(bill,new CampusFlow.Students.StudentProfile(bill.StudentProfileId,tenantId,null,
                new(CampusFlow.StudentInformationSystems.StudentInformationSystemProvider.ThesisElements,"0","CF004-TEST","cf004-test@my.nelson.edu","CampusFlow",null,"Test")),
                BillCompletionStep.StudentEmail,configuration,pdfGenerator.Generate(bill),"CF004-diagnostic.pdf");
            message.Subject="[NON-PRODUCTION] CampusFlow bill approval mail check";
            message.Headers.Add("X-CampusFlow-Operation-ID",operationId.ToString("D"));
            await sender.SendAsync(message);
            Notice=$"Azure accepted the test message and synthetic PDF for your signed-in school address. Check your inbox. Operation: {operationId:D}.";
            logger?.LogInformation("CF-004 self-test accepted for actor {ActorUserId}; operation {OperationId}",actor,operationId);
        }
        catch(BillCompletionPreparationException exception) { Notice=exception.Message; }
        catch(Exception exception)
        {
            Notice=$"The test send result is unconfirmed. Check Azure delivery logs before trying again. Operation: {operationId:D}.";
            logger?.LogWarning(exception,"CF-004 self-test unconfirmed; operation {OperationId}",operationId);
        }
        return RedirectToPage(new { StudentId });
    }
    public async Task<IActionResult> OnPostPrepareTestAsync(Guid id, bool confirmedTest = false)
    {
        if (!IsNonProduction || IsReadOnlyView || !confirmedTest || CurrentUser.Id is not Guid actor || CurrentTenant.Id is null) return Forbid();
        var approval=await Find(id);if(approval is null)return NotFound();
        var progress=BillApprovalCompletionProgress.Read(approval);
        if(progress.ActiveRunId.HasValue || progress.Steps.Values.Any(x=>x.Status is BillCompletionStatus.Running or BillCompletionStatus.Uncertain or BillCompletionStatus.Completed && x.EmailOperationId.HasValue)) return BadRequest();
        try { BillApprovalTestRecipient.Capture(approval,actor,CurrentUser.Email,DateTime.UtcNow); }
        catch(BillCompletionPreparationException exception) { Notice=exception.Message; return RedirectToPage(new { StudentId }); }
        await approvals.UpdateAsync(approval,autoSave:true);
        Notice="Nonprod test recipient captured from your signed-in account. Retry pending work when ready; no work has been sent or queued by this action.";
        return RedirectToPage(new { StudentId });
    }
    public async Task<IActionResult> OnPostAllowDocumentCreationAsync(Guid id, bool confirmedNoDocument = false)
    {
        if(IsReadOnlyView || !confirmedNoDocument || CurrentTenant.Id is null)return Forbid();
        var approval=await Find(id);if(approval is null)return NotFound();
        var progress=BillApprovalCompletionProgress.Read(approval);
        var artifact=await artifacts.FindAsync(x=>x.BillApprovalId==id && x.TenantId==CurrentTenant.Id);
        if(progress.ActiveRunId.HasValue || progress.Step(BillCompletionStep.Document).Status!=BillCompletionStatus.Failed ||
            !string.IsNullOrWhiteSpace(artifact?.ElementsDocumentTrackingId))return BadRequest();
        var step=progress.Step(BillCompletionStep.Document);step.DocumentCreationAttemptedAt=null;step.DocumentCreationAllowed=false;
        step.ReconciledBy=CurrentUser.Id;step.ReconciledAt=DateTime.UtcNow;progress.Save(approval);
        await approvals.UpdateAsync(approval,autoSave:true);
        Notice="Confirmed absence recorded. Retry pending work to allow another document creation attempt.";
        return RedirectToPage(new { StudentId });
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
    public sealed record DeliveryRow(Guid Id,string StudentId,string Term,DateTime ApprovedAt,BillApprovalCompletionProgress Progress, bool NoStoredDocument = false, string? TestRecipient = null);
}
