using System;
using System.Text.Json.Nodes;

namespace CampusFlow.Housing;

public static class HousingRateDefaults
{
    // Patch only the exact legacy placeholder. JSON fields, special rates, occupant
    // identities, capacities, notes and intentional-single flags are retained.
    public static (string Json, int Count) ReplaceUnreviewed(string json)
    {
        var rooms = JsonNode.Parse(json) as JsonArray ?? throw new ArgumentException("Invalid housing snapshot.");
        var count = 0;
        foreach (var node in rooms)
        {
            var room = node as JsonObject ?? throw new ArgumentException("Invalid room.");
            var occupants = room["Occupants"] as JsonArray ?? throw new ArgumentException("Missing occupants.");
            foreach (var item in occupants)
            {
                var occupant = item as JsonObject ?? throw new ArgumentException("Invalid occupant.");
                if (occupant["Rate"]?.GetValue<string>() != "Unreviewed") continue;
                occupant["Rate"] = "Standard"; count++;
            }
        }
        return count == 0 ? (json, 0) : (rooms.ToJsonString(), count);
    }
}
