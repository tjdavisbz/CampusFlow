using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CampusFlow.BillApprovals;
namespace CampusFlow.Bibliu;

public static class BibliuRules
{
    public static bool IsApproved(BillApproval approval) => approval.AcceptedAt.HasValue && approval.Status == BillApprovalStatus.Approved;
    public static decimal VoucherAmount(decimal balanceIncludingPendingAid, decimal used) =>
        Math.Max(0m, Math.Max(500m, -balanceIncludingPendingAid) - Math.Max(0m, used));
    public static string EventNumber(string store, bool vocationalRehabHold, decimal balance) =>
        store + (vocationalRehabHold ? "200" : balance < -500m ? "100" : "500");
    public static IReadOnlyList<string> ExpandedTerms(string selected)
    {
        var parts = selected.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var year) || year < 1901 || year > 9998)
            throw new ArgumentException("BibliU terms must use Spring, Summer or Fall followed by a year.");
        return parts[0] switch {
            "Spring" => [$"Fall {year - 1}", $"Spring {year}", $"Summer {year}"],
            "Summer" => [$"Spring {year}", $"Summer {year}", $"Fall {year}"],
            "Fall" => [$"Summer {year}", $"Fall {year}", $"Spring {year + 1}"],
            _ => throw new ArgumentException("Choose a Spring, Summer or Fall term.")
        };
    }
    public static string Pad(string? value, int length, char padding = ' ', bool left = false)
    {
        value ??= "";
        return left ? (new string(padding, length) + value)[^length..] : (value + new string(padding, length))[..length];
    }
    public static string Cents(decimal amount) => decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture).PadLeft(18, '0');
    public static string StudentRecordNumber(string store, string studentId)
    {
        if (studentId.Length is < 1 or > 13 || studentId.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("A POS student identifier must contain 1–13 digits.");
        return store + studentId.PadLeft(13, '0');
    }
}
