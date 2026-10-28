using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Chapel;

public sealed record ChapelEventWindow(DateTime StartLocal, DateTime EndLocal)
{
    public void Validate()
    {
        if (StartLocal.Kind != DateTimeKind.Unspecified || EndLocal.Kind != DateTimeKind.Unspecified ||
            EndLocal <= StartLocal || StartLocal.Date != EndLocal.Date)
            throw new ArgumentException("A chapel event must have same-day campus-local start and end times.");
    }
}

public enum ChapelScanMatchOutcome
{
    NoQualifyingScan,
    Present,
    Late,
    ScanInOnly,
    Ambiguous
}

public sealed record ChapelScanMatch(ChapelScanMatchOutcome Outcome,
    DateTime? ScanInLocal, DateTime? ScanOutLocal, int DistinctScanTimes);

/// <summary>
/// Produces a read-only scan decision for comparison with legacy chapel results.
/// All activity labels, including HCC Attendance, are eligible for the same windows.
/// The legacy procedure ignores Attendance_Checkout; so does this matcher. A single
/// timestamp can overlap scan-in/out windows, but is not enough evidence for an
/// automatic decision in the replacement and is marked ambiguous instead.
/// </summary>
public static class ChapelScanMatcher
{
    public static ChapelScanMatch Match(string studentId, ChapelEventWindow chapel,
        IEnumerable<ChapelScanCsvRow> scans)
    {
        chapel.Validate();
        if (string.IsNullOrWhiteSpace(studentId)) throw new ArgumentException("Student ID is required.", nameof(studentId));
        ArgumentNullException.ThrowIfNull(scans);

        var times = scans
            .Where(x => x.StudentId == studentId &&
                        x.ScannedAtLocal >= chapel.StartLocal.AddMinutes(-20) &&
                        x.ScannedAtLocal <= chapel.EndLocal.AddHours(1))
            .Select(x => x.ScannedAtLocal)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var earlyStart = chapel.StartLocal.AddMinutes(-20);
        var earlyEnd = chapel.StartLocal.AddMinutes(5);
        var lateStart = chapel.StartLocal.AddMinutes(6);
        var outStart = chapel.EndLocal.AddMinutes(-30);
        var outEnd = chapel.EndLocal.AddHours(1);

        var early = times.Where(x => x >= earlyStart && x <= earlyEnd).ToArray();
        var late = times.Where(x => x >= lateStart && x <= chapel.EndLocal).ToArray();
        var outs = times.Where(x => x >= outStart && x <= outEnd).ToArray();

        if (TryPair(early, outs, out var earlyIn, out var earlyOut))
            return new ChapelScanMatch(ChapelScanMatchOutcome.Present, earlyIn, earlyOut, times.Length);
        if (TryPair(late, outs, out var lateIn, out var lateOut))
            return new ChapelScanMatch(ChapelScanMatchOutcome.Late, lateIn, lateOut, times.Length);
        if ((early.Length > 0 || late.Length > 0) && outs.Length > 0)
            return new ChapelScanMatch(ChapelScanMatchOutcome.Ambiguous,
                early.Length > 0 ? early[0] : late[0], outs[0], times.Length);
        if (early.Length > 0)
            return new ChapelScanMatch(ChapelScanMatchOutcome.ScanInOnly, early[0], null, times.Length);
        return new ChapelScanMatch(ChapelScanMatchOutcome.NoQualifyingScan, null, null, times.Length);
    }

    private static bool TryPair(DateTime[] ins, DateTime[] outs, out DateTime scanIn, out DateTime scanOut)
    {
        foreach (var candidateIn in ins)
        {
            foreach (var candidateOut in outs)
            {
                if (candidateOut <= candidateIn) continue;
                scanIn = candidateIn;
                scanOut = candidateOut;
                return true;
            }
        }
        scanIn = default;
        scanOut = default;
        return false;
    }
}
