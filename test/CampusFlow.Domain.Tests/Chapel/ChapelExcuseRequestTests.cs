using System;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelExcuseRequestTests
{
    private static ChapelExcuseRequest Create(DateOnly start, DateOnly end,
        string category = "Work", string reason = "Work schedule") =>
        new(Guid.NewGuid(), Guid.NewGuid(), 268, 42, "20000000042", "Test Student",
            start, end, category, reason);

    [Fact]
    public void Accepts_one_to_three_days_and_preserves_student_request()
    {
        var request = Create(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 29),
            reason: "  Work schedule  ");
        request.StartDate.ShouldBe(new DateOnly(2026, 10, 27));
        request.EndDate.ShouldBe(new DateOnly(2026, 10, 29));
        request.Reason.ShouldBe("Work schedule");
    }

    [Theory]
    [InlineData(2026, 10, 26)]
    [InlineData(2026, 10, 30)]
    public void Rejects_dates_outside_one_to_three_days(int year, int month, int day)
    {
        Should.Throw<ArgumentException>(() =>
            Create(new DateOnly(2026, 10, 27), new DateOnly(year, month, day)));
    }

    [Fact]
    public void Rejects_unknown_category_and_missing_reason()
    {
        Should.Throw<ArgumentException>(() =>
            Create(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), "Unknown"));
        Should.Throw<ArgumentException>(() =>
            Create(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), reason: " "));
    }

    [Fact]
    public void Approval_records_reviewer_time_and_changed_attendance_count_once()
    {
        var request = Create(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 29));
        var reviewer = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc);

        request.Status.ShouldBe(ChapelExcuseRequestStatus.Pending);
        request.Approve(reviewer, approvedAt, 2);

        request.Status.ShouldBe(ChapelExcuseRequestStatus.Approved);
        request.ApprovedByUserId.ShouldBe(reviewer);
        request.ApprovedAt.ShouldBe(approvedAt);
        request.AttendanceRecordsExcused.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => request.Approve(reviewer, approvedAt, 2));
    }
}
