using System;
using System.Collections.Generic;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.BillApprovals;
public class BillApprovalNotificationTests
{
    private static readonly BillApproval Bill=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"10","100","269","B27S","Spring 2027");
    private static StudentProfile Profile(string email="student@nelson.edu")=>new(Guid.NewGuid(),Bill.TenantId,null,new(StudentInformationSystemProvider.ThesisElements,"10","100",email,"Test",null,"Student"));
    private static IConfiguration Config(bool nonprod=false,string? testRecipient=null)=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["App:EnvironmentLabel"]=nonprod?"NON-PRODUCTION":"",["BillApproval:Notifications:NonProductionRecipient"]=testRecipient }).Build();
    [Fact] public void Student_message_uses_legacy_sender_requested_bcc_and_exact_pdf()
    {
        using var message=BillApprovalNotification.Create(Bill,Profile(),BillCompletionStep.StudentEmail,Config(),new byte[]{1,2,3},"approved.pdf");
        Assert.Equal("StudentBilling@nelson.edu",message.From!.Address);Assert.Equal("student@nelson.edu",Assert.Single(message.To).Address);
        Assert.Equal("studentbilling@nelson.edu",Assert.Single(message.Bcc).Address);Assert.Single(message.Attachments);Assert.Contains("Congratulations",message.Body);
        Assert.DoesNotContain("charge up to",message.Body);
    }
    [Fact] public void Bookstore_message_uses_requested_recipient_and_does_not_disclose_student_pdf()
    {
        using var message=BillApprovalNotification.Create(Bill,Profile(),BillCompletionStep.BookstoreEmail,Config(),Array.Empty<byte>(),"");
        Assert.Equal("judahsoutfitters@nelson.edu",Assert.Single(message.To).Address);Assert.Equal("studentbilling@nelson.edu",Assert.Single(message.Bcc).Address);
        Assert.Empty(message.Attachments);Assert.Empty(message.CC);
    }
    [Fact] public void Nonproduction_redirects_every_message_to_the_explicit_test_recipient()
    {
        using var message=BillApprovalNotification.Create(Bill,Profile(),BillCompletionStep.StudentEmail,Config(true,"tester@nelson.edu"),new byte[]{1},"approved.pdf");
        Assert.Equal("tester@nelson.edu",Assert.Single(message.To).Address);Assert.Empty(message.Bcc);Assert.StartsWith("[NON-PRODUCTION]",message.Subject);
    }
    [Fact] public void Nonproduction_without_a_test_recipient_cannot_prepare_a_real_delivery()=>Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalNotification.Create(Bill,Profile(),BillCompletionStep.BookstoreEmail,Config(true),Array.Empty<byte>(),""));
    [Fact] public void A_personal_address_is_not_used_as_the_student_school_address()=>Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalNotification.Create(Bill,Profile("student@example.com"),BillCompletionStep.StudentEmail,Config(),new byte[]{1},"approved.pdf"));
}
