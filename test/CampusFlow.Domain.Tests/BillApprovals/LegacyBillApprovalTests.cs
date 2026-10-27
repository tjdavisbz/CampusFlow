using System;
using System.IO;
using CampusFlow.BillApprovals;
using Shouldly;
using Xunit;
namespace CampusFlow.BillApprovals;

public class LegacyBillApprovalTests
{
    private const string Header = "StudentUID\tTermCalendarID\tApproved\tLoginTime\tAcceptTime\n";
    [Fact]
    public void Import_preserves_status_nulls_sentinels_and_original_timestamp_precision()
    {
        var rows = LegacyBillApprovalFile.Read(new StringReader(Header +
            "10\t268\t1\t1900-01-01 00:00:00.000\t2026-10-21 14:02:40.123\n" +
            "11\t262\t1\t2024-08-01 12:00:00\tNULL\n" +
            "12\t265\t0\t2025-08-01 12:00:00\t2025-08-01 12:10:00\n"));
        rows.Count.ShouldBe(3);
        rows[0].LoginTime!.Value.Year.ShouldBe(1900);
        rows[0].AcceptTime!.Value.Millisecond.ShouldBe(123);
        rows[0].AcceptTime!.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
        rows[1].Approved.ShouldBeTrue(); rows[1].AcceptTime.ShouldBeNull();
        rows[2].Approved.ShouldBeFalse(); rows[2].AcceptTime.ShouldNotBeNull();
        rows[2].RowNumber.ShouldBe(4);
    }
    [Theory]
    [InlineData("10\t268\t2\tNULL\tNULL")]
    [InlineData("10\t268\t1\tinvalid-date\tNULL")]
    [InlineData("10\t-1\t1\tNULL\tNULL")]
    [InlineData("0\t268\t1\tNULL\tNULL")]
    [InlineData("10\t268\t1\tNULL")]
    public void Malformed_rows_fail_with_line_number_and_without_source_values(string row)
    {
        var error = Should.Throw<InvalidDataException>(() => LegacyBillApprovalFile.Read(new StringReader(Header + row)));
        error.Message.ShouldContain("line 2");
        error.Message.ShouldNotContain("invalid-date");
    }
    [Fact]
    public void Duplicate_student_term_pairs_are_rejected_instead_of_choosing_a_status()
    {
        Should.Throw<InvalidDataException>(() => LegacyBillApprovalFile.Read(new StringReader(Header +
            "10\t268\t1\tNULL\tNULL\n10\t268\t0\tNULL\tNULL\n")));
    }
    [Theory]
    [InlineData(true, true, "268", true)]
    [InlineData(true, false, "262", false)]
    [InlineData(false, true, "265", false)]
    [InlineData(true, true, "0", false)]
    public void Only_approved_dated_academic_records_count_as_approved(bool approved, bool dated, string term, bool expected)
    {
        var record = new LegacyBillApproval(Guid.NewGuid(), Guid.NewGuid(), "10", "100", term, "code", "term",
            approved, null, dated ? new DateTime(2026, 1, 1) : null, new string('a',64), 2);
        record.CountsAsApproved.ShouldBe(expected);
        record.SourceApproved.ShouldBe(approved);
    }
}
