using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.Web.BillApprovals;

public static class BillApprovalChargeOrdering
{
    // Presentation only: preserve the ledger, review fingerprint and stored snapshot order.
    public static IReadOnlyList<StudentBillingTransaction> ForDisplay(IEnumerable<StudentBillingTransaction> charges) =>
        charges.Where(x => !x.IsVoided)
            .OrderBy(x => Regex.IsMatch(x.Description.Trim(), @"^tuition\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? 0 : 1)
            .ThenBy(x => x.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TransactionDate)
            .ThenBy(x => x.ExternalTransactionId, StringComparer.Ordinal)
            .ToArray();
}
