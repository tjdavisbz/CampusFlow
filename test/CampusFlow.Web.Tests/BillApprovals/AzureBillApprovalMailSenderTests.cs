using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Communication.Email;
using CampusFlow.Web.BillApprovals;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;
namespace CampusFlow.BillApprovals;
public class AzureBillApprovalMailSenderTests
{
    [Fact] public async Task Azure_transport_preserves_recipients_bcc_and_attachment_without_mailbox_credentials()
    {
        var client=Substitute.For<EmailClient>();var operation=Substitute.For<EmailSendOperation>();
        operation.Value.Returns(EmailModelFactory.EmailSendResult("operation",EmailSendStatus.Succeeded));
        EmailMessage? sent=null;client.SendAsync(WaitUntil.Completed,Arg.Do<EmailMessage>(x=>sent=x),Arg.Any<CancellationToken>()).Returns(operation);
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"BillApproval:Notifications:AzureEndpoint","https://test.communication.azure.com/"}}).Build();
        var sender=new AzureBillApprovalMailSender(configuration,client);
        using var message=new MailMessage("studentbilling@nelson.edu","student@nelson.edu","Approved bill","Congratulations");message.Bcc.Add("studentbilling@nelson.edu");
        message.Attachments.Add(new Attachment(new System.IO.MemoryStream(new byte[]{1,2}),"approved.pdf","application/pdf"));
        await sender.SendAsync(message);Assert.NotNull(sent);Assert.Equal("studentbilling@nelson.edu",sent.SenderAddress);Assert.Single(sent.Recipients.BCC);Assert.Single(sent.Attachments);
    }
    [Theory][InlineData("http://test.communication.azure.com/")][InlineData("https://test.communication.azure.com.evil.test/")][InlineData("")]
    public void Invalid_endpoints_leave_transport_unconfigured(string endpoint)
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"BillApproval:Notifications:AzureEndpoint",endpoint}}).Build();
        Assert.False(new AzureBillApprovalMailSender(configuration).IsConfigured);
    }
}
