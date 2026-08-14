using System;
using CampusFlow.CourseSelections;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class CourseReviewTests
{
    [Fact]
    public void New_review_starts_pending_when_review_is_required()
    {
        var review = CreateReview(needsReview: true);

        review.Status.ShouldBe(CourseReviewStatus.Pending);
        review.NeedsReview.ShouldBeTrue();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.NotRequired);
    }

    [Fact]
    public void Approving_closes_review_without_removing_course()
    {
        var review = CreateReview(needsReview: true);
        var advisorUserId = Guid.NewGuid();

        review.Approve(advisorUserId, new DateTime(2026, 8, 14, 12, 0, 0), "Looks good");

        review.Status.ShouldBe(CourseReviewStatus.Approved);
        review.NeedsReview.ShouldBeFalse();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.NotRequired);
        review.AdvisorComment.ShouldBe("Looks good");
        review.DecidedByUserId.ShouldBe(advisorUserId);
    }

    [Fact]
    public void Rejection_remains_active_until_external_removal_completes()
    {
        var review = CreateReview(needsReview: true);
        var advisorUserId = Guid.NewGuid();

        review.BeginRejection(advisorUserId, new DateTime(2026, 8, 14, 12, 0, 0), "Choose another section");

        review.Status.ShouldBe(CourseReviewStatus.RejectionPending);
        review.NeedsReview.ShouldBeTrue();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.Pending);

        review.CompleteRejection();

        review.Status.ShouldBe(CourseReviewStatus.Rejected);
        review.NeedsReview.ShouldBeFalse();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.Completed);
    }

    [Fact]
    public void Failed_removal_stays_visible_for_retry()
    {
        var review = CreateReview(needsReview: true);
        review.BeginRejection(Guid.NewGuid(), new DateTime(2026, 8, 14, 12, 0, 0), null);

        review.RecordRemovalFailure(new DateTime(2026, 8, 14, 12, 1, 0), "Elements unavailable");

        review.Status.ShouldBe(CourseReviewStatus.Failed);
        review.NeedsReview.ShouldBeTrue();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.Failed);
        review.RemovalAttemptCount.ShouldBe(1);
        review.LastRemovalError.ShouldBe("Elements unavailable");
    }

    [Fact]
    public void Student_removal_closes_the_advisor_review()
    {
        var review = CreateReview(needsReview: true);
        var attemptedAt = new DateTime(2026, 8, 14, 12, 0, 0);

        review.BeginStudentRemoval(attemptedAt);

        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.Pending);
        review.RemovalAttemptCount.ShouldBe(1);
        review.LastRemovalAttemptAt.ShouldBe(attemptedAt);

        review.CompleteStudentRemoval();

        review.Status.ShouldBe(CourseReviewStatus.RemovedByStudent);
        review.NeedsReview.ShouldBeFalse();
        review.RemovalStatus.ShouldBe(ExternalCourseRemovalStatus.Completed);
    }

    private static CourseReview CreateReview(bool needsReview) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "13465", "2026FA",
        "4321", "9876", "Residential Undergraduate", "{}", "55",
        "advisor@nelson.edu", needsReview);
}
