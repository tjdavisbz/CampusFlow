using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.Housing;

public static class MealPlanCatalogRules
{
    // These are the reviewed Elements names for the existing portal choices.
    // Match complete names, never numeric prefixes (12 is not 120) or partial text.
    private static string[] Names(string name) => name.Trim().ToUpperInvariant() switch
    {
        "120 MEAL PLAN" => ["120 Meal Plan", "120 Block", "120 Block Meal Plan"],
        "20 MEAL PLAN" => ["20 Meal Plan", "20 Block Meal Plan"],
        "40 MEAL PLAN" => ["40 Meal Plan", "40 Block Meal Plan"],
        "60 MEAL PLAN" => ["60 Meal Plan", "60 Block Meal Plan"],
        "UNLIMITED MEAL PLAN" => ["Unlimited Meal Plan", "Unlimited Meals"],
        _ => [name.Trim()]
    };

    public static StudentMealPlanCatalogItem? Resolve(MealPlanConfiguration configuration,
        IEnumerable<StudentMealPlanCatalogItem> catalog, StudentMealPlanTerm term)
    {
        var names = Names(configuration.ExternalMealPlanName);
        var matches = catalog.Where(x => x.StartDate <= term.EndDate && x.EndDate >= term.StartDate)
            .Where(x => !configuration.ExternalMealPlanId.HasValue || x.MealPlanId == configuration.ExternalMealPlanId)
            .Where(x => names.Contains(WithoutTermSuffix(x.Name, term), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        // Multiple candidates require catalog review; never silently select a newer or cheaper plan.
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string WithoutTermSuffix(string name, StudentMealPlanTerm term)
    {
        name = name.Trim();
        var parts = term.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var year) || year != term.StartDate.Year)
            return name;
        var shortSeason = parts[0].ToUpperInvariant() switch
        {
            "SPRING" => "Spr", "SUMMER" => "Sum", "FALL" => "Fall", "WINTER" => "Win", _ => null
        };
        if (shortSeason is null) return name;
        var shortYear = (year % 100).ToString("D2", CultureInfo.InvariantCulture);
        foreach (var suffix in new[] { $" - {parts[0]} {year}", $" - {parts[0]} {shortYear}", $" - {shortSeason} {shortYear}" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length].Trim();
        return name;
    }
}
