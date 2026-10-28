using System;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelAttendanceRecordTests
{
    [Fact]
    public void Staff_decision_replaces_imported_status_and_records_its_source()
    {
        var record = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 27, 10, 0, 0),
            ChapelAttendanceStatus.Absent, "Scan in only", 123);

        record.SetByStaff(ChapelAttendanceStatus.Excused, "  Approved absence  ");

        record.Status.ShouldBe(ChapelAttendanceStatus.Excused);
        record.Comment.ShouldBe("Approved absence");
        record.Source.ShouldBe(ChapelAttendanceSource.Staff);
        record.LegacyAttendanceId.ShouldBe(123);
        record.ChapelAtLocal.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public void Staff_comment_has_a_bound()
    {
        var record = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 27, 10, 0, 0),
            ChapelAttendanceStatus.Pending, null);

        Should.Throw<ArgumentException>(() =>
            record.SetByStaff(ChapelAttendanceStatus.Present, new string('x', 4001)));
    }

    [Fact]
    public void Scheduled_row_accepts_one_cbord_decision_and_idempotent_retry()
    {
        var record = ChapelAttendanceRecord.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 28, 10, 15, 0));

        record.ApplyCbordDecision(ChapelAttendanceStatus.Absent, "  Late  ")
            .ShouldBe(ChapelImportDecisionResult.Applied);
        record.ApplyCbordDecision(ChapelAttendanceStatus.Absent, "Late")
            .ShouldBe(ChapelImportDecisionResult.AlreadyApplied);
        record.ApplyCbordDecision(ChapelAttendanceStatus.Present, null)
            .ShouldBe(ChapelImportDecisionResult.RequiresReview);
        record.Status.ShouldBe(ChapelAttendanceStatus.Absent);
        record.Comment.ShouldBe("Late");
        record.Source.ShouldBe(ChapelAttendanceSource.CbordImport);
    }

    [Fact]
    public void Import_cannot_rewrite_legacy_or_staff_decisions()
    {
        var legacy = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 23, 10, 15, 0),
            ChapelAttendanceStatus.Present, null, 123);
        legacy.ApplyCbordDecision(ChapelAttendanceStatus.Absent, "Late")
            .ShouldBe(ChapelImportDecisionResult.RequiresReview);
        legacy.Status.ShouldBe(ChapelAttendanceStatus.Present);

        var staff = ChapelAttendanceRecord.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 28, 10, 15, 0));
        staff.SetByStaff(ChapelAttendanceStatus.Excused, "Approved");
        staff.ApplyCbordDecision(ChapelAttendanceStatus.Present, null)
            .ShouldBe(ChapelImportDecisionResult.RequiresReview);
        staff.Status.ShouldBe(ChapelAttendanceStatus.Excused);
        staff.Comment.ShouldBe("Approved");
    }

    [Fact]
    public void Conflicting_legacy_flags_keep_original_values_and_portal_display_precedence()
    {
        var record = ChapelAttendanceRecord.CreateFromLegacy(Guid.NewGuid(), Guid.NewGuid(),
            48434, 268, 42, "20000000042", "Test Student",
            new DateTime(2026, 9, 4, 10, 15, 0), present: true, absent: true,
            excused: false, comment: null);

        record.Status.ShouldBe(ChapelAttendanceStatus.Present);
        record.Source.ShouldBe(ChapelAttendanceSource.LegacyImport);
        record.ExtraProperties["LegacyPresent"].ShouldBe(true);
        record.ExtraProperties["LegacyAbsent"].ShouldBe(true);
        record.ExtraProperties["LegacyExcused"].ShouldBe(false);
        record.ExtraProperties["LegacyFlagConflict"].ShouldBe(true);
    }

    [Fact]
    public void Approved_request_excuses_absence_and_preserves_previous_decision_metadata()
    {
        var record = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 27, 10, 0, 0),
            ChapelAttendanceStatus.Absent, "Late", 123);
        var requestId = Guid.NewGuid();

        record.ExcuseForApprovedRequest(requestId).ShouldBeTrue();

        record.Status.ShouldBe(ChapelAttendanceStatus.Excused);
        record.Source.ShouldBe(ChapelAttendanceSource.Staff);
        record.Comment.ShouldBe("Approved chapel excuse request");
        record.ExtraProperties["ApprovedChapelExcuseRequestId"].ShouldBe(requestId.ToString());
        record.ExtraProperties["PreExcuseRequestStatus"].ShouldBe("Absent");
        record.ExtraProperties["PreExcuseRequestComment"].ShouldBe("Late");
        record.LegacyAttendanceId.ShouldBe(123);
    }

    [Theory]
    [InlineData(ChapelAttendanceStatus.Present)]
    [InlineData(ChapelAttendanceStatus.Excused)]
    public void Approved_request_preserves_present_or_already_excused_sessions(ChapelAttendanceStatus status)
    {
        var record = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 27, 10, 0, 0),
            status, "Existing decision", 123);

        record.ExcuseForApprovedRequest(Guid.NewGuid()).ShouldBeFalse();

        record.Status.ShouldBe(status);
        record.Comment.ShouldBe("Existing decision");
        record.Source.ShouldBe(ChapelAttendanceSource.LegacyImport);
    }

    [Theory]
    [InlineData(ChapelAttendanceSource.LegacyImport)]
    [InlineData(ChapelAttendanceSource.CbordImport)]
    public void Scan_out_waiver_credits_only_imported_scan_in_only_absences(ChapelAttendanceSource source)
    {
        var record = new ChapelAttendanceRecord(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
            "20000000042", "Test Student", new DateTime(2026, 10, 28, 10, 15, 0),
            ChapelAttendanceStatus.Absent, "Scan In Only", 123);
        if (source == ChapelAttendanceSource.CbordImport)
        {
            var scheduled = ChapelAttendanceRecord.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), 268, 42,
                "20000000042", "Test Student", new DateTime(2026, 10, 28, 10, 15, 0));
            scheduled.ApplyCbordDecision(ChapelAttendanceStatus.Absent, "Scan In Only");
            record = scheduled;
        }

        record.CreditScanInOnlyWithoutScanOut(new DateTime(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc));

        record.Status.ShouldBe(ChapelAttendanceStatus.Present);
        record.Source.ShouldBe(ChapelAttendanceSource.Staff);
        record.Comment.ShouldBe("Scan-out waived for this chapel");
        record.ExtraProperties["PreScanOutWaiverSource"].ShouldBe(source.ToString());
        record.ExtraProperties["PreScanOutWaiverComment"].ShouldBe("Scan In Only");
    }

    [Theory]
    [InlineData(ChapelAttendanceStatus.Absent, ChapelAttendanceSource.CbordImport, "Late")]
    [InlineData(ChapelAttendanceStatus.Present, ChapelAttendanceSource.CbordImport, "Scan In Only")]
    [InlineData(ChapelAttendanceStatus.Absent, ChapelAttendanceSource.Staff, "Scan In Only")]
    [InlineData(ChapelAttendanceStatus.Excused, ChapelAttendanceSource.LegacyImport, "Scan In Only")]
    public void Scan_out_waiver_rejects_other_decisions(ChapelAttendanceStatus status,
        ChapelAttendanceSource source, string comment)
    {
        ChapelScanOutWaiverPolicy.IsEligible(status, source, comment).ShouldBeFalse();
    }
}
