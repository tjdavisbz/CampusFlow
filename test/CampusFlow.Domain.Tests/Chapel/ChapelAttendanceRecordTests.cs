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
}
