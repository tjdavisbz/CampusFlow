using System;
using CampusFlow.BillApprovals;
using Xunit;
namespace CampusFlow.BillApprovals;
public class BillApprovalTestRecipientTests
{
    private static BillApproval Bill() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "10", "100", "269", "B27C", "Spring 2027");
    [Theory]
    [InlineData("tester@nelson.edu")]
    [InlineData("student@my.nelson.edu")]
    public void The_server_captured_actor_survives_background_delivery_and_retries(string email)
    {
        var bill=Bill();var actor=Guid.NewGuid();BillApprovalTestRecipient.Capture(bill,actor,email,DateTime.UtcNow);
        var recorded=BillApprovalTestRecipient.Require(bill);Assert.Equal(actor,recorded.ActorUserId);Assert.Equal(email,recorded.Email);
        BillApprovalTestRecipient.Capture(bill,actor,email,DateTime.UtcNow);
        Assert.Equal(recorded,BillApprovalTestRecipient.Require(bill));
        Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalTestRecipient.Capture(bill,Guid.NewGuid(),"other@nelson.edu",DateTime.UtcNow));
        Assert.Equal(email,BillApprovalTestRecipient.Require(bill).Email);
    }
    [Theory]
    [InlineData("personal@example.test")]
    [InlineData("Name <tester@nelson.edu>")]
    [InlineData("tester@nelson.edu.evil.test")]
    [InlineData(null)]
    public void An_unverified_or_missing_actor_address_is_rejected(string? email) =>
        Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalTestRecipient.Capture(Bill(),Guid.NewGuid(),email,DateTime.UtcNow));
    [Fact]
    public void Historical_approvals_are_not_silently_rerouted_to_a_background_user_or_fixed_mailbox() =>
        Assert.Throws<BillCompletionPreparationException>(()=>BillApprovalTestRecipient.Require(Bill()));
    [Fact]
    public void An_uncertain_document_creation_cannot_be_automatically_created_again()
    {
        var p=new BillApprovalCompletionProgress();p.Finish(BillCompletionStep.Pdf,BillCompletionStatus.Completed);
        Assert.True(p.Start(BillCompletionStep.Document,DateTime.UtcNow));Assert.True(p.Step(BillCompletionStep.Document).DocumentCreationAllowed);
        p.Finish(BillCompletionStep.Document,BillCompletionStatus.Failed);p.ResetForRetry(DateTime.UtcNow,false);
        Assert.True(p.Start(BillCompletionStep.Document,DateTime.UtcNow));Assert.False(p.Step(BillCompletionStep.Document).DocumentCreationAllowed);
    }
    [Fact]
    public void Email_operation_identity_is_assigned_before_the_send_and_retained_for_unknown_results()
    {
        var p=new BillApprovalCompletionProgress();p.Finish(BillCompletionStep.Pdf,BillCompletionStatus.Completed);p.Finish(BillCompletionStep.Cleared,BillCompletionStatus.Completed);
        Assert.True(p.Start(BillCompletionStep.StudentEmail,DateTime.UtcNow));var operation=p.Step(BillCompletionStep.StudentEmail).EmailOperationId;Assert.NotNull(operation);
        p.Finish(BillCompletionStep.StudentEmail,BillCompletionStatus.Uncertain);p.ResetForRetry(DateTime.UtcNow,false);
        Assert.Equal(operation,p.Step(BillCompletionStep.StudentEmail).EmailOperationId);Assert.False(p.Start(BillCompletionStep.StudentEmail,DateTime.UtcNow));
    }
}
