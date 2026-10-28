using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace CampusFlow.Chapel;

public sealed record ChapelScanCsvRow(int RowNumber, string StudentId, string Activity,
    DateTime ScannedAtLocal, bool AttendanceCheckout, string TerminalName);

/// <summary>
/// Reads the five-column CBORD chapel export. The timestamp is campus-local, not UTC.
/// Parsing is deliberately strict so an unexpected vendor file is rejected for review
/// rather than silently changing attendance.
/// </summary>
public static class ChapelScanCsvReader
{
    private static readonly string[] ExpectedColumns =
        ["IDNUMBER", "ACTIVITY", "DATETIME", "ATTENDANCECHECKOUT", "TERMINALNAME"];
    private static readonly string[] TimestampFormats =
        ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"];

    public static IEnumerable<ChapelScanCsvRow> ReadRows(TextReader reader)
    {
        using var parser = new TextFieldParser(reader)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");

        var header = parser.ReadFields();
        if (header is null || header.Length != ExpectedColumns.Length ||
            !header.Select(NormalizeColumn).SequenceEqual(ExpectedColumns))
            throw new InvalidDataException("The CBORD chapel file header is not the expected five-column layout.");

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            string[]? fields;
            try { fields = parser.ReadFields(); }
            catch (MalformedLineException exception)
            {
                throw new InvalidDataException($"Malformed CBORD CSV at row {rowNumber}.", exception);
            }

            if (fields is null || fields.Length != 5)
                throw new InvalidDataException($"CBORD CSV row {rowNumber} must contain five fields.");

            var studentId = fields[0].Trim();
            var activity = fields[1].Trim();
            var terminal = fields[4].Trim();
            if (studentId.Length == 0 || studentId.Length > 64 || !studentId.All(char.IsAsciiDigit))
                throw new InvalidDataException($"CBORD CSV row {rowNumber} has an invalid ID_Number.");
            if (activity.Length == 0 || terminal.Length == 0)
                throw new InvalidDataException($"CBORD CSV row {rowNumber} is missing an activity or terminal.");
            if (!DateTime.TryParseExact(fields[2].Trim(), TimestampFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var scannedAt))
                throw new InvalidDataException($"CBORD CSV row {rowNumber} has an invalid Date_Time.");
            if (!bool.TryParse(fields[3].Trim(), out var checkout))
                throw new InvalidDataException($"CBORD CSV row {rowNumber} has an invalid Attendance_Checkout value.");

            yield return new ChapelScanCsvRow(rowNumber, studentId, activity,
                DateTime.SpecifyKind(scannedAt, DateTimeKind.Unspecified), checkout, terminal);
        }
    }

    private static string NormalizeColumn(string value) =>
        value.Trim().Trim('\uFEFF').Trim().Trim('[', ']').Replace(" ", "").Replace("_", "").ToUpperInvariant();
}
