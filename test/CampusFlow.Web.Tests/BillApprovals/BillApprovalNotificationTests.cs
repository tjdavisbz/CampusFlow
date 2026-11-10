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
    private static BillApproval NewBill()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"10","100","269","B27S","Spring 2027");
    private static StudentProfile Profile(BillApproval bill,string email="student@nelson.edu")=>new(Guid.NewGuid(),bill.TenantId,null,new(StudentInformationSystemProvider.ThesisElements,"10","100",email,"Test",null,"Student"));
    private static IConfiguration Config(bool nonprod=false,string? testRecipient=null)=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["App:EnvironmentLabel"]=nonprod?"NON-PRODUCTION":"",["BillApproval:Notifications:NonProductionRecipient"]=testRecipient }).Build();
    [Fact] public void Student_message_uses_legacy_sender_requested_bcc_and_exact_pdf()
    {
        var bill=NewBill();
        using var message=BillApprovalNotification.Create(bill,Profile(bill),BillCompletionStep.StudentEmail,Config(),new byte[]{1,2,3},"approved.pdf");
        Assert.Equal("StudentBilling@nelson.edu",message.From!.Address);Assert.Equal("student@nelson.edu",Assert.Single(message.To).Address);
        Assert.Equal("studentbilling@nelson.edu",Assert.Single(message.Bcc).Address);Assert.Single(message.Attachments);Assert.Contains("Congratulations",message.Body);
        Assert.DoesNotContain("charge up to",message.Body);
    }
    [Fact] public void Bookstore_message_uses_requested_recipient_and_does_not_disclose_student_pdf()
    {
        var bill=NewBill();
        using var message=BillApprovalNotification.Create(bill,Profile(bill),BillCompletionStep.BookstoreEmail,Config(),Array.Empty<byte>(),"");
        Assert.Equal("judahsoutfitters@nelson.edu",Assert.Single(message.To).Address);Assert.Equal("studentbilling@nelson.edu",Assert.Single(message.Bcc).Address);
        Assert.Empty(message.Attachments);Assert.Empty(message.CC);
    }
    [Fact] public void Nonproduction_redirects_to_the_captured_signed_in_actor_not_a_configured_fallback()
    {
        var bill=NewBill();
        BillApprovalTestRecipient.Capture(bill,Guid.NewGuid(),"tester@nelson.edu",DateTime.UtcNow);
        using var message=BillApprovalNotification.Create(bill,Profile(bill),BillCompletionStep.StudentEmail,Config(true,"wrong@nelson.edu"),new byte[]{1},"approved.pdf");
        Assert.Equal("tester@nelson.edu",Assert.Single(message.To).Address);Assert.Empty(message.Bcc);Assert.StartsWith("[NON-PRODUCTION]",message.Subject);
    }
    [Fact] public void Nonproduction_without_a_test_recipient_cannot_prepare_a_real_delivery()=>Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalNotification.Create(NewBill(),Profile(NewBill()),BillCompletionStep.BookstoreEmail,Config(true),Array.Empty<byte>(),""));
    [Fact] public void A_personal_address_is_not_used_as_the_student_school_address()=>Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalNotification.Create(NewBill(),Profile(NewBill(),"student@example.com"),BillCompletionStep.StudentEmail,Config(),new byte[]{1},"approved.pdf"));
    [Theory]
    [InlineData(200, "$500.00")]
    [InlineData(0, "$500.00")]
    [InlineData(-499, "$500.00")]
    [InlineData(-500, "$500.00")]
    [InlineData(-1250.75, "$1,250.75")]
    public void Bookstore_uses_legal_name_and_legacy_allowance_from_the_accepted_snapshot(decimal balance,string amount)
    {
        var bill=NewBill();bill.UpdateDraft(BillPaymentChoice.PayNow,0,0,0,balance,0,"[]","{}");
        var profile=new StudentProfile(Guid.NewGuid(),bill.TenantId,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@nelson.edu","Jeffrey","Jeff","Francis"));
        using var message=BillApprovalNotification.Create(bill,profile,BillCompletionStep.BookstoreEmail,Config(),Array.Empty<byte>(),"");
        Assert.Contains("Jeffrey Francis",message.Body);Assert.DoesNotContain("Jeff Francis",message.Body);Assert.Contains(amount,message.Body);Assert.DoesNotContain("current bookstore voucher process",message.Body);Assert.Contains("Only actual purchases",message.Body);Assert.Empty(message.Attachments);
    }
}
