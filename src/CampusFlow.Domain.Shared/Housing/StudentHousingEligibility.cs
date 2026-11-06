using System;

namespace CampusFlow.Housing;

public sealed record StudentHousingEligibility(string AttendanceType, string Campus, string? Building)
{
    private static bool Matches(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    public bool HasAssignment => !string.IsNullOrWhiteSpace(Building);
    public bool IsAmericanIndianCollege => Matches(AttendanceType, "American Indian College");
    public bool HousingRequired => IsAmericanIndianCollege || Matches(AttendanceType, "Residential Undergraduate")
        || Matches(Campus, "Oaks_MAIN") || HasAssignment;
    public bool MealPlanEligible => HousingRequired && !IsAmericanIndianCollege;
    public HousingChoice AssignedChoice => IsCommuterBuilding(Building) ? HousingChoice.Commuter : HousingChoice.OnCampus;
    public static bool IsCommuterBuilding(string? building) => Matches(building, "Commuter") || Matches(building, "Commuter Dorm");
}
