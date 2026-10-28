using System;
using System.IO;
using System.Linq;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelScanCsvReaderTests
{
    private const string Header = "[ID_Number] ,[Activity] ,[Date_Time],[Attendance_Checkout],[Terminal Name]\n";

    [Fact]
    public void ReadsPaddedVendorRowsWithoutConvertingLocalTimeToUtc()
    {
        using var source = new StringReader(Header +
            "20000000000              ,Chapel Attendance        ,2026-08-26 10:00:00,true ,Attendance Reader Chapel #12            \n");

        var scan = ChapelScanCsvReader.ReadRows(source).Single();

        scan.RowNumber.ShouldBe(2);
        scan.StudentId.ShouldBe("20000000000");
        scan.Activity.ShouldBe("Chapel Attendance");
        scan.AttendanceCheckout.ShouldBeTrue();
        scan.TerminalName.ShouldBe("Attendance Reader Chapel #12");
        scan.ScannedAtLocal.Kind.ShouldBe(DateTimeKind.Unspecified);
        scan.ScannedAtLocal.ShouldBe(new DateTime(2026, 8, 26, 10, 0, 0));
    }

    [Fact]
    public void RejectsUnexpectedLayoutInsteadOfGuessing()
    {
        using var source = new StringReader("Student,Time\n20000000000,2026-08-26 10:00:00\n");

        Should.Throw<InvalidDataException>(() => ChapelScanCsvReader.ReadRows(source).ToList());
    }

    [Fact]
    public void AcceptsBomAndQuotedCsvFields()
    {
        using var source = new StringReader("\uFEFF" + Header +
            "20000000000,\"Chapel, special event\",2026-08-26 10:00:00,false,Reader\n");

        ChapelScanCsvReader.ReadRows(source).Single().Activity.ShouldBe("Chapel, special event");
    }

    [Fact]
    public void PreservesHccActivityForScheduledEventMatching()
    {
        using var source = new StringReader(Header +
            "20000000000,HCC Attendance,2026-09-29 10:00:00,false,Attendance Reader HCC # 3\n");

        ChapelScanCsvReader.ReadRows(source).Single().Activity.ShouldBe("HCC Attendance");
    }

    [Fact]
    public void RejectsInvalidRowsWithLocation()
    {
        using var source = new StringReader(Header +
            "20000000000,Chapel Attendance,2026-08-26 10:00:00,false,Reader\n" +
            "20000000001,Chapel Attendance,bad-date,false,Reader\n");

        Should.Throw<InvalidDataException>(() => ChapelScanCsvReader.ReadRows(source).ToList())
            .Message.ShouldContain("row 3");
    }
}
