using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.Extensions.Configuration;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace CampusFlow.Web.BillApprovals;

public class BillApprovalCompletionOperations(IRepository<BillApprovalArtifact,Guid> artifacts,
    IRepository<StudentProfile,Guid> profiles, IBlobContainer<BillApprovalPdfContainer> blobs,
    IBillApprovalPdfGenerator generator, IGuidGenerator ids, IUnitOfWorkManager units, IConfiguration configuration,
    IStudentInformationSystemBillClearanceService clearance, IStudentInformationSystemDocumentTrackingService documents,
    IBillApprovalMailSender sender) : IBillApprovalCompletionOperations
{
    public bool IsEnabled(BillCompletionStep step) => step switch
    {
        BillCompletionStep.Pdf=>true,
        BillCompletionStep.Cleared=>configuration.GetValue<bool>("BillApproval:ClearedStatus:Enabled"),
        BillCompletionStep.Document=>configuration.GetValue<bool>("BillApproval:DocumentTracking:Enabled"),
        _=>configuration.GetValue<bool>("BillApproval:Notifications:Enabled") && sender.IsConfigured
    };
    public Task<bool> IsCompleteAsync(BillApproval approval,BillCompletionStep step)=>InUnit(async()=>
    {
        var artifact=await artifacts.FindAsync(x=>x.BillApprovalId==approval.Id && x.TenantId==approval.TenantId);
        return artifact is not null && step switch
        {
            BillCompletionStep.Pdf=>artifact.PdfStatus==BillArtifactOperationStatus.Completed,
            BillCompletionStep.Document=>artifact.DocumentUploadStatus==BillArtifactOperationStatus.Completed,
            BillCompletionStep.StudentEmail=>artifact.StudentEmailStatus==BillArtifactOperationStatus.Completed,
            BillCompletionStep.BookstoreEmail=>artifact.BillingEmailStatus==BillArtifactOperationStatus.Completed,
            _=>false
        };
    });
    private async Task<T> InUnit<T>(Func<Task<T>> work)
    {using var unit=units.Begin(requiresNew:true,isTransactional:true);var value=await work();await unit.CompleteAsync();return value;}
    private async Task<BillApprovalArtifact> Artifact(BillApproval approval)
    {
        var artifact=await artifacts.FindAsync(x=>x.BillApprovalId==approval.Id && x.TenantId==approval.TenantId);
        if(artifact is null){artifact=new(ids.Create(),approval.TenantId,approval.Id);await artifacts.InsertAsync(artifact,autoSave:true);}
        return artifact;
    }
    private Task<(byte[] Bytes,string Name)> Pdf(BillApproval approval)=>InUnit(async()=>
    {
        var artifact=await Artifact(approval);
        if(artifact.PdfStatus==BillArtifactOperationStatus.Completed && !string.IsNullOrWhiteSpace(artifact.PdfBlobName))
        {
            var bytes=await blobs.GetAllBytesAsync(artifact.PdfBlobName);
            if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=artifact.PdfSha256)
                throw new BillCompletionPreparationException("The approved PDF failed its integrity check. Contact an administrator.");
            return (bytes,artifact.PdfFileName!);
        }
        var pdf=generator.Generate(approval);var name=$"Approved-Bill-{string.Concat(approval.StudentId.Where(char.IsLetterOrDigit))}-{string.Concat(approval.TermCode.Where(char.IsLetterOrDigit))}.pdf";
        var blob=$"{approval.Id:N}/{name}";await blobs.SaveAsync(blob,pdf,overrideExisting:true);
        artifact.MarkPdfCompleted(name,blob,Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(),DateTime.UtcNow);
        await artifacts.UpdateAsync(artifact,autoSave:true);return(pdf,name);
    });
    public async Task ExecuteAsync(BillApproval approval,BillCompletionStep step)
    {
        if(!IsEnabled(step))throw new BillCompletionPreparationException("This completion step has not been enabled.");
        if(step==BillCompletionStep.Pdf){await Pdf(approval);return;}
        if(step==BillCompletionStep.Cleared){await clearance.MarkClearedAsync(approval.ExternalStudentId,approval.ExternalTermId);return;}
        if(step==BillCompletionStep.Document)
        {
            var pdf=await Pdf(approval);
            var id=await InUnit(async()=>{var a=await Artifact(approval);return a.ElementsDocumentTrackingId;});
            if(string.IsNullOrWhiteSpace(id))
            {
                id=await documents.CreateApprovedBillAsync(new(approval.ExternalStudentId,approval.TermName,approval.Id,approval.AcceptedAt!.Value));
                await InUnit(async()=>{var a=await Artifact(approval);a.MarkDocumentCreated(id,DateTime.UtcNow);await artifacts.UpdateAsync(a,autoSave:true);return true;});
            }
            if(!await documents.HasImageAsync(id))await documents.UploadImageAsync(id,pdf.Name,pdf.Bytes);
            await InUnit(async()=>{var a=await Artifact(approval);a.MarkDocumentUploadCompleted(DateTime.UtcNow);await artifacts.UpdateAsync(a,autoSave:true);return true;});return;
        }
        var profile=await InUnit(()=>profiles.GetAsync(approval.StudentProfileId));
        if(profile.TenantId!=approval.TenantId || profile.ExternalStudentId!=approval.ExternalStudentId)
            throw new BillCompletionPreparationException("The student profile does not match the approved bill.");
        var attachment=step==BillCompletionStep.StudentEmail?await Pdf(approval):(Array.Empty<byte>(),string.Empty);
        using var message=BillApprovalNotification.Create(approval,profile,step,configuration,attachment.Item1,attachment.Item2);
        // The coordinator persisted Running before this call. An unknown result is never automatically resent.
        await sender.SendAsync(message);
        await InUnit(async()=>{var a=await Artifact(approval);a.MarkEmailAccepted(step==BillCompletionStep.BookstoreEmail,DateTime.UtcNow);await artifacts.UpdateAsync(a,autoSave:true);return true;});
    }
}

public static class BillApprovalNotification
{
    public static MailMessage Create(BillApproval approval,StudentProfile profile,BillCompletionStep step,IConfiguration configuration,byte[] pdf,string fileName)
    {
        try
        {
            string Setting(string key,string fallback)=>configuration["BillApproval:Notifications:"+key]??fallback;
            MailAddress Address(string value)
            {
                if(!MailAddress.TryCreate(value,out var address)||!string.Equals(address.Address,value,StringComparison.OrdinalIgnoreCase))
                    throw new BillCompletionPreparationException("A notification address is invalid.");
                return address;
            }
            var from=Address(Setting("FromAddress","StudentBilling@nelson.edu"));
            var billing=Address(Setting("BillingBcc","studentbilling@nelson.edu"));
            var bookstore=step==BillCompletionStep.BookstoreEmail;
            if(!bookstore && step!=BillCompletionStep.StudentEmail)throw new BillCompletionPreparationException("Unknown notification type.");
            var target=bookstore?Address(Setting("BookstoreAddress","judahsoutfitters@nelson.edu")):Address(profile.Email);
            var domains=configuration.GetSection("BillApproval:Notifications:StudentEmailDomains").Get<string[]>()??["nelson.edu"];
            if(!bookstore&&!domains.Contains(target.Host,StringComparer.OrdinalIgnoreCase))
                throw new BillCompletionPreparationException("The student needs a verified Nelson email address before notification.");
            if(!bookstore&&(pdf.Length==0||string.IsNullOrWhiteSpace(fileName)))throw new BillCompletionPreparationException("The approved PDF is required before emailing the student.");
            var message=new MailMessage {From=from,IsBodyHtml=false,
                Subject=bookstore?$"Bill approval completed - {approval.TermName}":"Approved Bill and Schedule",
                Body=bookstore?$"{profile.DisplayName} ({approval.StudentId}) has completed bill approval for {approval.TermName}.\n\nApproval reference: {approval.Id:D}\nPlease use the current bookstore voucher process for the student's available purchasing amount.":
                    $"Congratulations, {profile.DisplayName}! Your bill approval for {approval.TermName} is complete.\n\nYour approved bill and schedule are attached. Keep this copy for your records.\n\nPlease contact Student Billing with any questions."};
            var nonprod=string.Equals(configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase)||
                string.Equals(configuration["ASPNETCORE_ENVIRONMENT"],"Staging",StringComparison.OrdinalIgnoreCase);
            if(nonprod)
            {
                var test=Address(Setting("NonProductionRecipient",string.Empty));
                if(!domains.Contains(test.Host,StringComparer.OrdinalIgnoreCase))throw new BillCompletionPreparationException("A Nelson test recipient is required in non-production.");
                message.To.Add(test);message.Subject="[NON-PRODUCTION] "+message.Subject;
            }
            else {message.To.Add(target);message.Bcc.Add(billing);}message.Headers.Add("X-CampusFlow-Delivery-ID",$"{approval.Id:N}-{step}");
            if(!bookstore)message.Attachments.Add(new Attachment(new MemoryStream(pdf,false),fileName,"application/pdf"));
            return message;
        }
        catch(BillCompletionPreparationException){throw;}
        catch(Exception){throw new BillCompletionPreparationException("The notification could not be prepared. No email was sent.");}
    }
}
