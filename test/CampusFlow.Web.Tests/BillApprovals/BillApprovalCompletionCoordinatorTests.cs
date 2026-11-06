using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Web.BillApprovals;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.BillApprovals;
public class BillApprovalCompletionCoordinatorTests
{
    private readonly BillApproval _bill;private readonly IBillApprovalCompletionOperations _operations=Substitute.For<IBillApprovalCompletionOperations>();
    private readonly ICurrentTenant _tenant=Substitute.For<ICurrentTenant>();private readonly BillApprovalCompletionCoordinator _coordinator;
    public BillApprovalCompletionCoordinatorTests()
    {
        var owner=Guid.NewGuid();_tenant.Id.Returns(owner);_bill=new(Guid.NewGuid(),owner,Guid.NewGuid(),Guid.NewGuid(),"10","100","269","B27S","Spring 2027");
        _bill.Accept(Guid.NewGuid(),1,"Agreement",DateTime.UtcNow,null,null);
        var repo=Substitute.For<IRepository<BillApproval,Guid>>();repo.GetAsync(_bill.Id,Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(_bill);
        _operations.IsEnabled(Arg.Any<BillCompletionStep>()).Returns(true);
        _coordinator=new(repo,_tenant,Substitute.For<IUnitOfWorkManager>(),_operations);
    }
    [Fact] public async Task Completion_runs_each_step_once_and_retains_native_approval()
    {
        await _coordinator.RunAsync(_bill.Id);await _coordinator.RunAsync(_bill.Id);
        foreach(var step in Enum.GetValues<BillCompletionStep>())await _operations.Received(1).ExecuteAsync(_bill,step);
        Assert.True(BillApprovalCompletionProgress.Read(_bill).IsComplete);Assert.Equal(BillApprovalStatus.Approved,_bill.Status);
    }
    [Fact] public async Task An_unknown_email_outcome_does_not_resend_on_a_job_retry_or_disable_enable_cycle()
    {
        _operations.ExecuteAsync(_bill,BillCompletionStep.StudentEmail).Returns(Task.FromException(new TimeoutException()));
        await _coordinator.RunAsync(_bill.Id);
        Assert.Equal(BillCompletionStatus.Uncertain,BillApprovalCompletionProgress.Read(_bill).Step(BillCompletionStep.StudentEmail).Status);
        _operations.IsEnabled(BillCompletionStep.StudentEmail).Returns(false);await _coordinator.RunAsync(_bill.Id);
        _operations.IsEnabled(BillCompletionStep.StudentEmail).Returns(true);await _coordinator.RunAsync(_bill.Id);
        await _operations.Received(1).ExecuteAsync(_bill,BillCompletionStep.StudentEmail);
    }
    [Fact] public async Task The_running_claim_is_saved_before_email_transport_is_called()
    {
        _operations.ExecuteAsync(_bill,BillCompletionStep.BookstoreEmail).Returns(_=>
        {Assert.Equal(BillCompletionStatus.Running,BillApprovalCompletionProgress.Read(_bill).Step(BillCompletionStep.BookstoreEmail).Status);return Task.CompletedTask;});
        await _coordinator.RunAsync(_bill.Id);
    }
    [Fact] public async Task Disabled_steps_are_not_executed()
    {
        _operations.IsEnabled(Arg.Any<BillCompletionStep>()).Returns(false);await _coordinator.RunAsync(_bill.Id);
        Assert.DoesNotContain(_operations.ReceivedCalls(),x=>x.GetMethodInfo().Name=="ExecuteAsync");
    }
    [Fact] public async Task Already_confirmed_artifact_delivery_is_reconciled_without_sending_again()
    {
        _operations.IsCompleteAsync(_bill,Arg.Any<BillCompletionStep>()).Returns(true);await _coordinator.RunAsync(_bill.Id);
        Assert.True(BillApprovalCompletionProgress.Read(_bill).IsComplete);
        Assert.DoesNotContain(_operations.ReceivedCalls(),x=>x.GetMethodInfo().Name=="ExecuteAsync");
    }
    [Fact] public async Task Another_tenants_approval_cannot_trigger_completion()
    {
        _tenant.Id.Returns(Guid.NewGuid());await Assert.ThrowsAsync<InvalidOperationException>(()=>_coordinator.RunAsync(_bill.Id));Assert.Empty(_operations.ReceivedCalls());
    }
}
