using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Students;
using CampusFlow.StudentInformationSystems;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.Application.Tests.BillApprovals;

public class BillApprovalCompletionLookupTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task Native_approval_or_dated_legacy_approval_counts_but_a_local_reset_supersedes_legacy(bool nativeApproved, bool legacyApproved, bool expected)
    {
        var owner = Guid.NewGuid();var tenant = Substitute.For<ICurrentTenant>();tenant.Id.Returns(owner);
        var profile = new StudentProfile(Guid.NewGuid(), owner, Guid.NewGuid(), new(StudentInformationSystemProvider.ThesisElements, "10", "100", "student@example.test", "Test", null, "Student"));
        var approvals = Substitute.For<IRepository<BillApproval, Guid>>();var legacy = Substitute.For<IRepository<LegacyBillApproval, Guid>>();
        if(nativeApproved)
        {
            var row=new BillApproval(Guid.NewGuid(),owner,profile.UserId!.Value,profile.Id,"10","100","269","B27S","Spring 2027");
            row.UpdateDraft(BillPaymentChoice.NoBalanceDue,0,0,0,0,0,"[]","{}");
            row.Accept(Guid.NewGuid(),1,"Agreement",DateTime.UtcNow,null,null);
            approvals.FindAsync(Arg.Any<Expression<Func<BillApproval,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(c => c.Arg<Expression<Func<BillApproval,bool>>>().Compile()(row) ? row : null);
        }
        legacy.AnyAsync(Arg.Any<Expression<Func<LegacyBillApproval,bool>>>(),Arg.Any<CancellationToken>()).Returns(legacyApproved);
        var lookup=new BillApprovalCompletionLookup(approvals,legacy,tenant);
        Assert.Equal(expected,await lookup.IsApprovedAsync(profile,"269"));
        var draft=new BillApproval(Guid.NewGuid(),owner,profile.UserId!.Value,profile.Id,"10","100","269","B27S","Spring 2027");
        approvals.FindAsync(Arg.Any<Expression<Func<BillApproval,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(draft);
        Assert.False(await lookup.IsApprovedAsync(profile,"269"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_context_and_another_tenants_profile_cannot_supply_completion(bool host)
    {
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(host ? null : Guid.NewGuid());
        var profile=new StudentProfile(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
        var approvals=Substitute.For<IRepository<BillApproval,Guid>>();var legacy=Substitute.For<IRepository<LegacyBillApproval,Guid>>();
        Assert.False(await new BillApprovalCompletionLookup(approvals,legacy,tenant).IsApprovedAsync(profile,"269"));
        Assert.Empty(approvals.ReceivedCalls());Assert.Empty(legacy.ReceivedCalls());
    }
}
