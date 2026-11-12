using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.Web.Pages;

// Presentation only: award decisions must retain the provider's original Amount.
public static class FinancialAidDisplay
{
    public static decimal? Amount(StudentFinancialAidAward award) =>
        award.Provider == StudentInformationSystemProvider.ThesisElements
            ? award.DistributionAmount is >= 0m ? award.DistributionAmount : null
            : award.Amount;

    public static decimal? Total(IEnumerable<StudentFinancialAidAward> awards)
    {
        decimal total = 0;
        foreach (var award in awards)
        {
            if (Amount(award) is not { } amount) return null;
            total += amount;
        }
        return total;
    }

    public static string Currency(decimal? amount) => amount.HasValue
        ? Formatting.UsdCurrency.Format(amount.Value)
        : "Amount unavailable";
}
