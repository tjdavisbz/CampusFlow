using System;
using System.Text.Json;
using CampusFlow.AdvisorPortal;
using CampusFlow.CourseSelections;
using CampusFlow.Web.Portals;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class AdvisorReviewSendGuardTests
{
    private static readonly Guid FirstReview = Guid.Parse("4f5f1030-37b2-40e7-b7f5-6774cf6e5090");
    private static readonly Guid SecondReview = Guid.Parse("f7e31864-df0a-4687-ae75-40b295ed8752");

    [Fact]
    public void Blocks_Unchanged_Review_Even_When_Previous_Message_Field_Starts_Empty()
    {
        var sent = SentSubmission("Please tell me your graduation date.",
            new { ReviewId = FirstReview, Decision = (string?)null, Comment = (string?)null });
        var current = Input(new AdvisorCourseDecisionInput { ReviewId = FirstReview });

        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeTrue();
        current.OverallComment = "Please tell me your graduation date.";
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeTrue();
    }

    [Fact]
    public void Allows_New_Message_Or_Decision_Or_Course()
    {
        var sent = SentSubmission(null,
            new { ReviewId = FirstReview, Decision = (string?)null, Comment = (string?)null });
        var current = Input(new AdvisorCourseDecisionInput { ReviewId = FirstReview });

        current.OverallComment = "Can we talk about graduation?";
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeFalse();
        current.OverallComment = null;
        current.Courses[0].Decision = "Approve";
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeFalse();
        current.Courses[0].Decision = null;
        current.Courses[0].Comment = "Please check the prerequisite.";
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeFalse();
        current.Courses[0].Comment = null;
        current.Courses.Add(new AdvisorCourseDecisionInput { ReviewId = SecondReview });
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeFalse();
    }

    [Fact]
    public void Ignores_Already_Decided_Courses_That_Left_Pending_List()
    {
        var sent = SentSubmission(null,
            new { ReviewId = FirstReview, Decision = "Approve", Comment = (string?)null },
            new { ReviewId = SecondReview, Decision = (string?)null, Comment = (string?)null });
        var current = Input(new AdvisorCourseDecisionInput { ReviewId = SecondReview });

        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeTrue();
        current.Courses.Clear();
        AdvisorReviewSendGuard.AlreadySent(current, sent).ShouldBeFalse();
    }

    private static SubmitAdvisorReviewInput Input(params AdvisorCourseDecisionInput[] courses) => new()
    {
        StudentProfileId = Guid.NewGuid(),
        ExternalTermId = "269",
        Courses = [.. courses]
    };

    private static CourseReviewSubmission SentSubmission(string? message, params object[] decisions)
    {
        var submission = new CourseReviewSubmission(Guid.NewGuid(), null, Guid.NewGuid(), "269",
            Guid.NewGuid(), "advisor@nelson.edu", message, JsonSerializer.Serialize(decisions),
            DateTime.UtcNow, ReviewEmailStatus.Pending);
        submission.RecordEmailResult(DateTime.UtcNow, true, null);
        return submission;
    }
}
