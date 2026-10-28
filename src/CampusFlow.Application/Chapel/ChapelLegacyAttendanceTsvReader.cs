using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace CampusFlow.Chapel;

public sealed record ChapelLegacyAttendanceRow(int RowNumber, int LegacyAttendanceId,
    int TermCalendarId, DateTime ChapelAtLocal, int LegacyStudentUid, string StudentId,
    bool Present, bool Absent, bool Excused, string? Comment, string LastName,
    string FirstName, string? MiddleName)
{
    public bool HasConflictingFlags => (Present ? 1 : 0) + (Absent ? 1 : 0) + (Excused ? 1 : 0) > 1;
}

/// <summary>
/// Strictly reads the read-only CAMS chapel history export. This does not persist data.
/// Import callers must independently map and verify legacy StudentUID/StudentID against
/// the Elements student record before creating a ChapelAttendanceRecord.
/// </summary>
public static class ChapelLegacyAttendanceTsvReader
{
    private static readonly string[] ExpectedColumns =
    [
        "ChapelAttendanceID", "TermCalendarID", "CADate", "LegacyStudentUID",
        "StudentID", "Present", "Absent", "Excused", "Comment", "Lastname",
        "Firstname", "Middle", "DateOnly"
    ];
    private static readonly string[] TimestampFormats =
        ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"];

    public static IEnumerable<ChapelLegacyAttendanceRow> ReadRows(TextReader reader)
    {
        using var parser = new TextFieldParser(reader)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters("\t");

        var header = parser.ReadFields();
        if (header is null || !header.Select(x => x.Trim().Trim('\uFEFF')).SequenceEqual(ExpectedColumns))
            throw new InvalidDataException("The chapel history export has an unexpected header.");

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            string[]? fields;
            try { fields = parser.ReadFields(); }
            catch (MalformedLineException exception)
            {
                throw new InvalidDataException($"Malformed chapel history row {rowNumber}.", exception);
            }
            if (fields is null || fields.Length != ExpectedColumns.Length)
                throw new InvalidDataException($"Chapel history row {rowNumber} must contain 13 fields.");

            if (!int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var attendanceId) || attendanceId <= 0 ||
                !int.TryParse(fields[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var termId) || termId <= 0 ||
                !int.TryParse(fields[3].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var studentUid) || studentUid <= 0)
                throw new InvalidDataException($"Chapel history row {rowNumber} has an invalid numeric identifier.");

            if (!DateTime.TryParseExact(fields[2].Trim(), TimestampFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var chapelAt) ||
                !DateTime.TryParseExact(fields[12].Trim(), "MM/dd/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dateOnly) || chapelAt.Date != dateOnly.Date)
                throw new InvalidDataException($"Chapel history row {rowNumber} has an invalid or inconsistent chapel date.");

            var studentId = fields[4].Trim();
            if (studentId.Length == 0 || studentId.Length > 64 || !studentId.All(char.IsAsciiDigit))
                throw new InvalidDataException($"Chapel history row {rowNumber} has an invalid StudentID.");
            if (!TryFlag(fields[5], out var present) || !TryFlag(fields[6], out var absent) ||
                !TryFlag(fields[7], out var excused))
                throw new InvalidDataException($"Chapel history row {rowNumber} has an invalid status flag.");

            var comment = fields[8];
            var lastName = fields[9].Trim();
            var firstName = fields[10].Trim();
            var middleName = fields[11].Trim();
            if (comment.Length > 4000 || lastName.Length == 0 || firstName.Length == 0)
                throw new InvalidDataException($"Chapel history row {rowNumber} has an invalid comment or name.");

            yield return new ChapelLegacyAttendanceRow(rowNumber, attendanceId, termId,
                DateTime.SpecifyKind(chapelAt, DateTimeKind.Unspecified), studentUid,
                studentId, present, absent, excused, comment.Length == 0 ? null : comment,
                lastName, firstName, middleName.Length == 0 ? null : middleName);
        }
    }

    private static bool TryFlag(string text, out bool value)
    {
        switch (text.Trim())
        {
            case "0": value = false; return true;
            case "1": value = true; return true;
            default: value = false; return false;
        }
    }
}
