using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Web.BillApprovals;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.BillApprovals;

public class LegacyBillApprovalLookupTests
{
    [Fact]
    public async Task Lookup_keeps_all_history_but_only_qualifies_dated_approved_rows_in_its_tenant_and_term()
    {
        var tenant = Substitute.For<ICurrentTenant>(); var tenantId = Guid.NewGuid(); tenant.Id.Returns(tenantId);
        LegacyBillApproval Row(Guid owner,string uid,string term,bool approved,bool dated) =>
            new(Guid.NewGuid(),owner,uid,"student",term,"code","term",approved,null,dated?DateTime.Now:null,new string('a',64),2);
        var rows = new[] {Row(tenantId,"10","268",true,true),Row(tenantId,"10","262",true,true),
            Row(tenantId,"11","268",false,true),Row(tenantId,"12","268",true,false),Row(Guid.NewGuid(),"10","268",true,true)};
        var repo = Substitute.For<IRepository<LegacyBillApproval,Guid>>();
        repo.GetListAsync(Arg.Any<Expression<Func<LegacyBillApproval,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(c=>rows.Where(c.Arg<Expression<Func<LegacyBillApproval,bool>>>().Compile()).ToList());
        var lookup = new LegacyBillApprovalLookup(repo,tenant);
        (await lookup.ApprovedStudentIdsAsync("268")).ShouldBe(new[]{"10"});
        (await lookup.HistoryAsync("10")).Count.ShouldBe(2);
        (await lookup.ApprovedStudentIdsAsync("0")).ShouldBeEmpty();
        tenant.Id.Returns((Guid?)null);repo.ClearReceivedCalls();
        (await lookup.ApprovedStudentIdsAsync("268")).ShouldBeEmpty();
        (await lookup.HistoryAsync("10")).ShouldBeEmpty();
        (await lookup.FindAsync("10","268")).ShouldBeNull();
        repo.ReceivedCalls().ShouldBeEmpty();
    }
}
