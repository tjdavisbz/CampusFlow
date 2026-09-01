using System;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class CourseSelectionOperationTests
{
    [Fact]
    public void Failure_after_external_registration_requires_reconciliation()
    {
        var operation = CreateOperation();
        operation.RecordAttempt(new DateTime(2026, 8, 14, 12, 0, 0));
        operation.RecordExternalRegistration("9876");

        operation.Fail("Review persistence failed");

        operation.Status.ShouldBe(CourseSelectionOperationStatus.ReconciliationRequired);
        operation.ExternalCourseRegistrationId.ShouldBe("9876");
        operation.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void Completed_operation_links_to_review()
    {
        var operation = CreateOperation();
        var reviewId = Guid.NewGuid();
        operation.RecordExternalRegistration("9876");

        operation.Complete(reviewId);

        operation.Status.ShouldBe(CourseSelectionOperationStatus.Completed);
        operation.CourseReviewId.ShouldBe(reviewId);
    }

    [Fact]
    public void Uncertain_external_failure_requires_reconciliation()
    {
        var operation = CreateOperation();

        operation.Fail("The registration response was interrupted", externalRegistrationMayHaveCompleted: true);

        operation.Status.ShouldBe(CourseSelectionOperationStatus.ReconciliationRequired);
    }

    private static CourseSelectionOperation CreateOperation() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "13465", "300", "400",
        Guid.NewGuid().ToString("N"), "{}");
}
