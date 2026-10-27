using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Students;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.BillApprovals;

public class BillApprovalHistoryTests : CampusFlowWebTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_is_scoped_to_the_current_student_profile_and_tenant(bool wrongTenant)
    {
        var tenant=GetRequiredService<ICurrentTenant>();var owner=Guid.NewGuid();
        using var tenantScope=tenant.Change(owner);
        var profile=new StudentProfile(Guid.NewGuid(),wrongTenant?Guid.NewGuid():owner,Guid.NewGuid(),
            new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
        var current=Substitute.For<ICurrentStudentView>();current.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        var repo=Substitute.For<IRepository<BillApproval,Guid>>();
        var rows=new[]{new BillApproval(Guid.NewGuid(),owner,profile.UserId,profile.Id,"10","100","268","B26Q","Fall 2026"),
            new BillApproval(Guid.NewGuid(),owner,Guid.NewGuid(),Guid.NewGuid(),"11","101","268","B26Q","Fall 2026")};
        repo.GetListAsync(Arg.Any<Expression<Func<BillApproval,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(c=>rows.Where(c.Arg<Expression<Func<BillApproval,bool>>>().Compile()).ToList());
        var legacy=Substitute.For<ILegacyBillApprovalLookup>();
        legacy.HistoryAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{
            new LegacyBillApproval(Guid.NewGuid(),owner,"10","100","7","B06Q","Fall 2006",true,null,null,new string('a',64),2)});
        var model=new BillApprovalHistoryModel(current,repo,legacy)
        {
            LazyServiceProvider=GetRequiredService<IAbpLazyServiceProvider>(),
            PageContext=new PageContext{HttpContext=new DefaultHttpContext()}
        };
        await model.OnGetAsync();
        if(wrongTenant)
        {
            model.HasProfile.ShouldBeFalse();model.Rows.ShouldBeEmpty();
            repo.ReceivedCalls().ShouldBeEmpty();legacy.ReceivedCalls().ShouldBeEmpty();
        }
        else
        {
            model.Rows.Count.ShouldBe(2);
            model.Rows.Single(r=>r.Source=="Previous portal").Note!.ShouldContain("missing");
            await legacy.Received(1).HistoryAsync("10",Arg.Any<CancellationToken>());
        }
    }
}
