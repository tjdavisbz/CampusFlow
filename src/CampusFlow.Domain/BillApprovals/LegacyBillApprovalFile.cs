using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CampusFlow.BillApprovals;

public sealed record LegacyBillApprovalSourceRow(int StudentUid, int TermCalendarId, bool Approved,
    DateTime? LoginTime, DateTime? AcceptTime, int RowNumber);

public static class LegacyBillApprovalFile
{
    public static IReadOnlyList<LegacyBillApprovalSourceRow> Read(TextReader reader)
    {
        if (reader.ReadLine()?.TrimStart('\uFEFF') != "StudentUID\tTermCalendarID\tApproved\tLoginTime\tAcceptTime")
            throw new InvalidDataException("Expected the five SAGU_ApprovedBills columns, tab separated.");
        var result = new List<LegacyBillApprovalSourceRow>();
        var keys = new HashSet<(int, int)>();
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split('\t');
            if (fields.Length != 5 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var student) || student <= 0 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var term) || term < 0 ||
                fields[2] is not ("0" or "1"))
                throw new InvalidDataException($"Invalid legacy approval at source line {lineNumber}.");
            if (!keys.Add((student, term)))
                throw new InvalidDataException($"Duplicate student/term at source line {lineNumber}.");
            result.Add(new(student, term, fields[2] == "1", Date(fields[3], lineNumber), Date(fields[4], lineNumber), lineNumber));
        }
        if (result.Count == 0) throw new InvalidDataException("The legacy approval file contains no rows.");
        return result;
    }

    private static DateTime? Date(string text, int row)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;
        if (!DateTime.TryParseExact(text, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Invalid timestamp at source line {row}.");
        return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
    }
}
