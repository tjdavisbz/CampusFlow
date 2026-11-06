using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Pages.Admin.StudentBilling;
using CampusFlow.Students;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.BillApprovals;
public class BillApprovalDeliveryReadinessTests
{
    [Theory]
    [InlineData(false,false,"staff@nelson.edu",false)]
    [InlineData(true,true,"staff@nelson.edu",false)]
    [InlineData(true,false,"personal@example.test",false)]
    [InlineData(true,false,"staff@nelson.edu",true)]
    public async Task Self_test_uses_only_the_actual_school_account_without_creating_or_queueing_a_bill(bool nonprod,bool viewing,string email,bool shouldSend)
    {
        var sender=Substitute.For<IBillApprovalMailSender>();sender.IsConfigured.Returns(true);
        bool recipientVerified=false;
        sender.SendAsync(Arg.Do<System.Net.Mail.MailMessage>(m=>recipientVerified=m.To.Count==1&&m.To[0].Address==email&&m.Bcc.Count==0&&m.Attachments.Count==1)).Returns(Task.CompletedTask);
        var pdf=Substitute.For<IBillApprovalPdfGenerator>();
        pdf.Generate(Arg.Do<BillApproval>(b=>Assert.Equal("CampusFlow Test",JsonSerializer.Deserialize<BillApprovalReviewSnapshot>(b.ReviewSnapshotJson)!.StudentName))).Returns(new byte[]{1,2,3});
        var approvals=Substitute.For<IRepository<BillApproval,Guid>>();var artifacts=Substitute.For<IRepository<BillApprovalArtifact,Guid>>();var jobs=Substitute.For<IBackgroundJobManager>();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["App:EnvironmentLabel"]=nonprod?"NON-PRODUCTION":"PRODUCTION",["BillApproval:Notifications:Enabled"]="true"}).Build();
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());user.Email.Returns(email);
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var context=new DefaultHttpContext();if(viewing)context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(StudentViewClaimTypes.ExternalStudentId,"414708")}));
        var model=new BillApprovalDeliveryModel(approvals,artifacts,jobs,Substitute.For<IAsyncQueryableExecuter>(),config,sender,pdf)
            {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=context}};
        var result=await model.OnPostTestEmailAsync();
        if(shouldSend){Assert.IsType<RedirectToPageResult>(result);await sender.Received(1).SendAsync(Arg.Any<System.Net.Mail.MailMessage>());Assert.True(recipientVerified);Assert.Contains("accepted",model.Notice);}
        else await sender.DidNotReceiveWithAnyArgs().SendAsync(default!);
        Assert.Empty(approvals.ReceivedCalls());Assert.Empty(artifacts.ReceivedCalls());Assert.Empty(jobs.ReceivedCalls());
    }
    [Fact]
    public void The_synthetic_test_snapshot_generates_a_real_pdf()
    {
        var environment=Substitute.For<IWebHostEnvironment>();environment.WebRootPath.Returns("/private/tmp/cf004-no-logo");
        var bill=new BillApproval(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0","CF004-TEST","0","TEST","Readiness check");
        bill.UpdateDraft(BillPaymentChoice.NoBalanceDue,0,0,0,0,0,"[]",JsonSerializer.Serialize(new BillApprovalReviewSnapshot("CampusFlow Test",0,[],[],[])));
        bill.Accept(Guid.NewGuid(),1,"Synthetic check",DateTime.UtcNow,null,null);
        var bytes=new MigraDocBillApprovalPdfGenerator(environment).Generate(bill);
        Assert.Equal("%PDF",Encoding.ASCII.GetString(bytes,0,4));Assert.True(bytes.Length>1000);
    }
}
