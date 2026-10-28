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
public sealed record HousingRateAmount(string Building, string RateChoice, decimal BaseAmount,
    decimal SingleSurchargeAmount);
public sealed record HousingRateDefinition(string Building, string RateChoice, string FeeName);

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

    public static IReadOnlyList<HousingRateDefinition> GetRateDefinitions(int termCalendarId)
    {
        if (termCalendarId != 268)
            throw new ArgumentException("Housing charge rules have not been reviewed for this term.");
        var definitions = Fall2026Fees.Select(x => new HousingRateDefinition(x.Key, "Standard", x.Value)).ToList();
        definitions.Add(new("Alta M. Washburn Building", "AIC Triple Room", "AIC Triple Room"));
        definitions.Add(new("Theodore Gannon Dormitory", "AIC Triple Room", "AIC Triple Room"));
        definitions.Add(new("Savell Hall", "Savell Triple Occupancy", "Savell Triple Occupancy"));
        return definitions;
    }

    public static IReadOnlyList<HousingRateAmount> SuggestRates(HousingPricingContext pricing)
    {
        return GetRateDefinitions(pricing.TermCalendarId).Select(definition =>
        {
            var matches = pricing.Fees.Where(x => string.Equals(x.Name.Trim(), definition.FeeName,
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1 || matches[0].TransactionDocumentId <= 0)
                throw new ArgumentException($"A unique transaction document for {definition.FeeName} is required in Elements to set up housing rates.");
            var basis = definition.Building is "Alta M. Washburn Building" or "Theodore Gannon Dormitory"
                ? matches[0].Amount - 1800m : matches[0].Amount;
            return new HousingRateAmount(definition.Building, definition.RateChoice, matches[0].Amount,
                basis > 0 ? decimal.Round(basis * 0.85m, 2, MidpointRounding.AwayFromZero) : 0m);
        }).ToList();
    }

    public static IReadOnlyList<HousingChargeLine> Quote(int termCalendarId, string building,
        string rateChoice, bool intentionalSingle, IReadOnlyCollection<HousingTermFee> feeMetadata,
        IReadOnlyCollection<HousingRateAmount> configuredRates,
        int singleTransactionDocumentId, int singleAccountsReceivableTypeId = 0)
    {
        if (singleTransactionDocumentId <= 0)
            throw new ArgumentException("The Single transaction document is not configured.");
        var definition = GetRateDefinitions(termCalendarId).SingleOrDefault(x =>
            string.Equals(x.Building, building.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RateChoice, rateChoice, StringComparison.Ordinal));
        if (definition is null)
            throw new ArgumentException($"The {rateChoice} rate is not approved for {building}.");
        var rates = configuredRates.Where(x => x.Building == definition.Building &&
            x.RateChoice == definition.RateChoice).ToList();
        if (rates.Count != 1 || rates[0].BaseAmount <= 0 || rates[0].SingleSurchargeAmount < 0)
            throw new ArgumentException($"Save a positive CampusFlow {rateChoice} rate for {building} before applying charges.");
        var matches = feeMetadata.Where(x => string.Equals(x.Name.Trim(), definition.FeeName,
            StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1 || matches[0].TransactionDocumentId <= 0)
            throw new ArgumentException($"A unique transaction document for {definition.FeeName} is required in Elements.");
        var baseFee = matches[0];
        var charges = new List<HousingChargeLine>
        {
            new($"Housing — {building} ({definition.FeeName})", baseFee.TransactionDocumentId,
                rates[0].BaseAmount, baseFee.AccountsReceivableTypeId)
        };
        if (intentionalSingle)
        {
            if (rates[0].SingleSurchargeAmount <= 0)
                throw new ArgumentException($"Save a positive CampusFlow intentional-single surcharge for {building} before applying it.");
            charges.Add(new("Intentional single room", singleTransactionDocumentId,
                rates[0].SingleSurchargeAmount,
                singleAccountsReceivableTypeId));
        }
        return charges;
    }
}
