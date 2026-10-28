using System;
using System.Collections.Generic;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelScanMatcherTests
{
    private static readonly ChapelEventWindow Event = new(
        new DateTime(2026, 9, 18, 9, 30, 0), new DateTime(2026, 9, 18, 10, 15, 0));

    [Fact]
    public void HccScansUseTheSameEarlyAndExitWindows()
    {
        var match = ChapelScanMatcher.Match("20000000000", Event,
            [Scan(9, 15, "HCC Attendance"), Scan(9, 50, "HCC Attendance")]);

        match.Outcome.ShouldBe(ChapelScanMatchOutcome.Present);
        match.ScanInLocal.ShouldBe(new DateTime(2026, 9, 18, 9, 15, 0));
        match.ScanOutLocal.ShouldBe(new DateTime(2026, 9, 18, 9, 50, 0));
    }

    [Fact]
    public void LateEntryWithExitIsNotPresent()
    {
        var match = ChapelScanMatcher.Match("20000000000", Event,
            [Scan(9, 40), Scan(10, 0)]);

        match.Outcome.ShouldBe(ChapelScanMatchOutcome.Late);
    }

    [Fact]
    public void EarlyEntryWithoutExitIsScanInOnly()
    {
        ChapelScanMatcher.Match("20000000000", Event, [Scan(9, 20)])
            .Outcome.ShouldBe(ChapelScanMatchOutcome.ScanInOnly);
    }

    [Fact]
    public void DuplicateTimestampCannotSatisfyBothWindows()
    {
        var shortEvent = new ChapelEventWindow(
            new DateTime(2026, 9, 18, 9, 30, 0), new DateTime(2026, 9, 18, 9, 50, 0));

        var match = ChapelScanMatcher.Match("20000000000", shortEvent,
            [Scan(9, 30), Scan(9, 30)]);

        match.Outcome.ShouldBe(ChapelScanMatchOutcome.Ambiguous);
        match.DistinctScanTimes.ShouldBe(1);
    }

    [Fact]
    public void ScanAtWindowBoundariesIsAccepted()
    {
        var match = ChapelScanMatcher.Match("20000000000", Event,
            [Scan(9, 10), Scan(11, 15)]);

        match.Outcome.ShouldBe(ChapelScanMatchOutcome.Present);
    }

    [Fact]
    public void LateScanWithoutExitCannotBeMarkedPresent()
    {
        ChapelScanMatcher.Match("20000000000", Event, [Scan(9, 36)])
            .Outcome.ShouldBe(ChapelScanMatchOutcome.NoQualifyingScan);
    }

    [Fact]
    public void CheckoutFlagDoesNotChangeTheLegacyTimingDecision()
    {
        var scans = new[]
        {
            Scan(9, 20) with { AttendanceCheckout = true },
            Scan(10, 0) with { AttendanceCheckout = false }
        };

        ChapelScanMatcher.Match("20000000000", Event, scans)
            .Outcome.ShouldBe(ChapelScanMatchOutcome.Present);
    }

    [Fact]
    public void ExitWindowMayExtendIntoTheNextDay()
    {
        var lateEvent = new ChapelEventWindow(
            new DateTime(2026, 9, 18, 23, 20, 0), new DateTime(2026, 9, 18, 23, 50, 0));
        var scans = new[]
        {
            Scan(23, 10),
            new ChapelScanCsvRow(3, "20000000000", "Chapel Attendance",
                new DateTime(2026, 9, 19, 0, 20, 0), false, "Reader")
        };

        ChapelScanMatcher.Match("20000000000", lateEvent, scans)
            .Outcome.ShouldBe(ChapelScanMatchOutcome.Present);
    }

    [Fact]
    public void OtherStudentsAndDatesDoNotMatch()
    {
        var scans = new List<ChapelScanCsvRow>
        {
            Scan(9, 20, studentId: "20000000001"),
            new(3, "20000000000", "Chapel Attendance", new DateTime(2026, 9, 17, 9, 20, 0), false, "Reader")
        };

        ChapelScanMatcher.Match("20000000000", Event, scans)
            .Outcome.ShouldBe(ChapelScanMatchOutcome.NoQualifyingScan);
    }

    private static ChapelScanCsvRow Scan(int hour, int minute,
        string activity = "Chapel Attendance", string studentId = "20000000000") =>
        new(2, studentId, activity, new DateTime(2026, 9, 18, hour, minute, 0), false, "Reader");
}
