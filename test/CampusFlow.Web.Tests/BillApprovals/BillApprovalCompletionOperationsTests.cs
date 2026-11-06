using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.BillApprovals;
public class BillApprovalCompletionOperationsTests
{
    private readonly BillApproval _bill;private readonly BillApprovalArtifact _artifact;
    private readonly IStudentInformationSystemDocumentTrackingService _documents=Substitute.For<IStudentInformationSystemDocumentTrackingService>();
    private readonly IBillApprovalMailSender _sender=Substitute.For<IBillApprovalMailSender>();
    private readonly BillApprovalCompletionOperations _operations;
    private static readonly byte[] PdfBytes=[1,2,3];
    public BillApprovalCompletionOperationsTests()
    {
        var owner=Guid.NewGuid();var profile=new StudentProfile(Guid.NewGuid(),owner,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@nelson.edu","Test",null,"Student"));
        _bill=new(Guid.NewGuid(),owner,Guid.NewGuid(),profile.Id,"10","100","269","B27S","Spring 2027");_bill.Accept(Guid.NewGuid(),1,"Agreement",DateTime.UtcNow,null,null);
        _artifact=new(Guid.NewGuid(),owner,_bill.Id);_artifact.MarkPdfCompleted("approved.pdf","blob",Convert.ToHexString(SHA256.HashData(PdfBytes)).ToLowerInvariant(),DateTime.UtcNow);
        var artifacts=Substitute.For<IRepository<BillApprovalArtifact,Guid>>();artifacts.FindAsync(Arg.Any<Expression<Func<BillApprovalArtifact,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(_artifact);
        var profiles=Substitute.For<IRepository<StudentProfile,Guid>>();profiles.GetAsync(profile.Id,Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(profile);
        var blobs=Substitute.For<IBlobContainer<BillApprovalPdfContainer>>();blobs.GetAsync("blob",Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream(PdfBytes)));
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {["BillApproval:Notifications:Enabled"]="true",["BillApproval:DocumentTracking:Enabled"]="true"}).Build();
        _sender.IsConfigured.Returns(true);_documents.CreateApprovedBillAsync(Arg.Any<StudentDocumentTrackingRequest>(),Arg.Any<CancellationToken>()).Returns("doc-123");
        _operations=new(artifacts,profiles,blobs,Substitute.For<IBillApprovalPdfGenerator>(),Substitute.For<IGuidGenerator>(),Substitute.For<IUnitOfWorkManager>(),configuration,
            Substitute.For<IStudentInformationSystemBillClearanceService>(),_documents,_sender);
    }
    [Fact] public async Task A_document_id_is_retained_after_upload_failure_and_not_created_again_on_retry()
    {
        _documents.UploadImageAsync("doc-123","approved.pdf",Arg.Any<byte[]>(),Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>_operations.ExecuteAsync(_bill,BillCompletionStep.Document));Assert.Equal("doc-123",_artifact.ElementsDocumentTrackingId);
        _documents.UploadImageAsync("doc-123","approved.pdf",Arg.Any<byte[]>(),Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await _operations.ExecuteAsync(_bill,BillCompletionStep.Document);
        await _documents.Received(1).CreateApprovedBillAsync(Arg.Is<StudentDocumentTrackingRequest>(x=>x.ApprovalId==_bill.Id),Arg.Any<CancellationToken>());
        Assert.Equal(BillArtifactOperationStatus.Completed,_artifact.DocumentUploadStatus);
    }
    [Fact] public async Task An_existing_image_is_not_uploaded_again()
    {
        _artifact.MarkDocumentCreated("doc-123",DateTime.UtcNow);_documents.HasImageAsync("doc-123",Arg.Any<CancellationToken>()).Returns(true);
        await _operations.ExecuteAsync(_bill,BillCompletionStep.Document);
        await _documents.DidNotReceiveWithAnyArgs().UploadImageAsync(default!,default!,default!);
    }
    [Fact] public async Task The_student_email_attaches_the_preserved_pdf_and_records_acceptance()
    {
        byte[]? attached=null;
        _sender.SendAsync(Arg.Do<MailMessage>(m=>{using var bytes=new System.IO.MemoryStream();m.Attachments[0].ContentStream.CopyTo(bytes);attached=bytes.ToArray();})).Returns(Task.CompletedTask);
        await _operations.ExecuteAsync(_bill,BillCompletionStep.StudentEmail);
        Assert.Equal(PdfBytes,attached);Assert.Equal(BillArtifactOperationStatus.Completed,_artifact.StudentEmailStatus);
    }
    [Fact] public async Task A_transport_timeout_is_not_recorded_as_accepted()
    {
        _sender.SendAsync(Arg.Any<MailMessage>()).Returns(Task.FromException(new TimeoutException()));
        await Assert.ThrowsAsync<TimeoutException>(()=>_operations.ExecuteAsync(_bill,BillCompletionStep.StudentEmail));
        Assert.Equal(BillArtifactOperationStatus.Pending,_artifact.StudentEmailStatus);
    }
    [Fact] public async Task Existing_confirmed_artifact_states_are_visible_to_the_coordinator()
    {
        _artifact.MarkEmailAccepted(true,DateTime.UtcNow);
        Assert.True(await _operations.IsCompleteAsync(_bill,BillCompletionStep.BookstoreEmail));Assert.False(await _operations.IsCompleteAsync(_bill,BillCompletionStep.StudentEmail));
    }
}
