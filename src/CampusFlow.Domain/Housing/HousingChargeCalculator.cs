using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

public sealed record HousingTermFee(string Name, int TransactionDocumentId, decimal Amount,
    int AccountsReceivableTypeId = 0);
public sealed record HousingChargeLine(string Description, int TransactionDocumentId, decimal Amount,
    int AccountsReceivableTypeId = 0);
public sealed record HousingPricingContext(int TermCalendarId, IReadOnlyCollection<HousingTermFee> Fees,
    int SingleTransactionDocumentId, int SingleAccountsReceivableTypeId = 0);

// The legacy Fall 2026 fee labels identify the charge for each Elements property.
// Future terms must have their own reviewed mapping; never reuse this one implicitly.
public static class HousingChargeCalculator
{
    private static readonly IReadOnlyDictionary<string, string> Fall2026Fees =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alta M. Washburn Building"] = "Washburn",
            ["Bridges Hall"] = "Bridges/Teeter",
            ["Collins Hall"] = "Collins",
            ["Guynes Hall"] = "Guynes/Kendrick",
            ["Kendrick Hall"] = "Guynes/Kendrick",
            ["Regents Apartment"] = "Regents",
            ["Savell Hall"] = "Savell",
            ["Teeter Hall"] = "Bridges/Teeter",
            ["Theodore Gannon Dormitory"] = "Gannon"
        };

    public static IReadOnlyList<HousingChargeLine> Quote(int termCalendarId, string building,
        string rateChoice, bool intentionalSingle, IReadOnlyCollection<HousingTermFee> feeSchedule,
        int singleTransactionDocumentId, int singleAccountsReceivableTypeId = 0)
    {
        if (termCalendarId != 268)
            throw new ArgumentException("Housing charge rules have not been reviewed for this term.");
        if (singleTransactionDocumentId <= 0)
            throw new ArgumentException("The Single transaction document is not configured.");
        if (!Fall2026Fees.TryGetValue(building.Trim(), out var feeName))
            throw new ArgumentException($"No Fall 2026 housing fee is mapped for {building}.");
        if (rateChoice is "AIC Triple Room")
        {
            if (building is not ("Alta M. Washburn Building" or "Theodore Gannon Dormitory"))
                throw new ArgumentException("AIC Triple Room is valid only for AIC housing.");
            feeName = "AIC Triple Room";
        }
        else if (rateChoice is "Savell Triple Occupancy")
        {
            if (!building.Equals("Savell Hall", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Savell Triple Occupancy is valid only for Savell Hall.");
            feeName = "Savell Triple Occupancy";
        }
        else if (rateChoice is not "Standard")
            throw new ArgumentException("Choose a reviewed housing rate before applying charges.");

        var matches = feeSchedule.Where(x =>
            string.Equals(x.Name.Trim(), feeName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1 || matches[0].TransactionDocumentId <= 0 || matches[0].Amount <= 0)
            throw new ArgumentException($"A unique positive Fall 2026 fee named {feeName} is required in Elements.");
        var baseFee = matches[0];
        var charges = new List<HousingChargeLine>
        {
            new($"Housing — {building} ({feeName})", baseFee.TransactionDocumentId,
                baseFee.Amount, baseFee.AccountsReceivableTypeId)
        };
        if (intentionalSingle)
        {
            var surchargeBase = building is "Alta M. Washburn Building" or "Theodore Gannon Dormitory"
                ? baseFee.Amount - 1800m : baseFee.Amount;
            if (surchargeBase <= 0)
                throw new ArgumentException("The intentional-single surcharge basis is not positive.");
            var surcharge = decimal.Round(surchargeBase * 0.85m, 2, MidpointRounding.AwayFromZero);
            charges.Add(new("Intentional single room", singleTransactionDocumentId, surcharge,
                singleAccountsReceivableTypeId));
        }
        return charges;
    }
}
