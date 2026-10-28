using System;
using System.IO;
using System.Linq;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelLegacyAttendanceTsvReaderTests
{
    private const string Header = "ChapelAttendanceID\tTermCalendarID\tCADate\tLegacyStudentUID\tStudentID\tPresent\tAbsent\tExcused\tComment\tLastname\tFirstname\tMiddle\tDateOnly\n";

    [Fact]
    public void Reads_conflicting_legacy_flags_without_discarding_either_one()
    {
        var row = Read("48434\t268\t2026-09-04 10:15:00.000\t42\t00000000042\t1\t1\t0\t\tStudent\tTest\t\t09/04/2026\n");

        row.LegacyAttendanceId.ShouldBe(48434);
        row.StudentId.ShouldBe("00000000042");
        row.HasConflictingFlags.ShouldBeTrue();
        row.Present.ShouldBeTrue();
        row.Absent.ShouldBeTrue();
        row.Comment.ShouldBeNull();
        row.ChapelAtLocal.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public void Rejects_inconsistent_date_only_column()
    {
        Should.Throw<InvalidDataException>(() =>
            Read("1\t268\t2026-09-04 10:15:00.000\t42\t00000000042\t1\t0\t0\t\tStudent\tTest\t\t09/05/2026\n"));
    }

    [Fact]
    public void Rejects_unexpected_status_flag()
    {
        Should.Throw<InvalidDataException>(() =>
            Read("1\t268\t2026-09-04 10:15:00.000\t42\t00000000042\tYes\t0\t0\t\tStudent\tTest\t\t09/04/2026\n"));
    }

    private static ChapelLegacyAttendanceRow Read(string data) =>
        ChapelLegacyAttendanceTsvReader.ReadRows(new StringReader(Header + data)).Single();
}
