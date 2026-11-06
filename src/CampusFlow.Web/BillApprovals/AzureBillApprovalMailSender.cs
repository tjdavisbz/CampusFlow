using System;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using CampusFlow.BillApprovals;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.BillApprovals;

public interface IBillApprovalMailSender
{
    bool IsConfigured { get; }
    Task SendAsync(MailMessage message);
}

// No mailbox credentials or ACS access keys: authentication is the App Service's managed identity.
public class AzureBillApprovalMailSender : IBillApprovalMailSender
{
    private readonly IConfiguration _configuration;
    private readonly EmailClient? _testClient;
    public AzureBillApprovalMailSender(IConfiguration configuration) { _configuration=configuration; }
    internal AzureBillApprovalMailSender(IConfiguration configuration,EmailClient client) { _configuration=configuration;_testClient=client; }
    private Uri? Endpoint => Uri.TryCreate(_configuration["BillApproval:Notifications:AzureEndpoint"],UriKind.Absolute,out var uri) &&
        uri.Scheme=="https" && uri.IsDefaultPort && uri.Host.EndsWith(".communication.azure.com",StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath=="/" && uri.Query.Length==0 && uri.Fragment.Length==0 && uri.UserInfo.Length==0 ? uri : null;
    public bool IsConfigured => Endpoint is not null;
    public async Task SendAsync(MailMessage message)
    {
        if(Endpoint is null)throw new BillCompletionPreparationException("Azure email has not been configured.");
        var options=new EmailClientOptions();options.Retry.MaxRetries=0;
        var client=_testClient??new EmailClient(Endpoint,new ManagedIdentityCredential(),options);
        var recipients=new EmailRecipients(message.To.Select(x=>new EmailAddress(x.Address)),
            message.CC.Select(x=>new EmailAddress(x.Address)),message.Bcc.Select(x=>new EmailAddress(x.Address)));
        var email=new EmailMessage(message.From!.Address,recipients,new EmailContent(message.Subject){PlainText=message.Body});
        email.UserEngagementTrackingDisabled=true;
        foreach (var address in message.ReplyToList) email.ReplyTo.Add(new EmailAddress(address.Address));
        foreach(var key in message.Headers.AllKeys)
            if(key is not null && key.StartsWith("X-CampusFlow-",StringComparison.Ordinal))email.Headers.Add(key,message.Headers[key]);
        foreach(var attachment in message.Attachments)
        {
            var bytes=await BinaryData.FromStreamAsync(attachment.ContentStream);
            email.Attachments.Add(new EmailAttachment(attachment.Name,attachment.ContentType.MediaType,bytes));
        }
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var operation = Guid.TryParse(message.Headers["X-CampusFlow-Operation-ID"], out var operationId)
            ? await client.SendAsync(WaitUntil.Completed, email, operationId, timeout.Token)
            : await client.SendAsync(WaitUntil.Completed, email, timeout.Token);
        if(operation.Value.Status!=EmailSendStatus.Succeeded)throw new InvalidOperationException("Azure email did not confirm the send operation.");
        // Succeeded confirms the service send operation, not delivery into a recipient's inbox.
    }
}
