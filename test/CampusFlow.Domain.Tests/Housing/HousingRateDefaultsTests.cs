using System.Text.Json.Nodes;
using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Domain.Tests.Housing;

public class HousingRateDefaultsTests
{
    [Fact]
    public void Repair_changes_only_placeholder_rates_and_is_idempotent()
    {
        const string json = """[{"Id":1,"Capacity":3,"Occupants":[{"StudentUid":10,"Rate":"Unreviewed","IntentionalSingle":true,"Notes":"Keep"},{"StudentUid":11,"Rate":"AIC Triple Room","Notes":"Special"},{"StudentUid":12,"Rate":"Standard"}]}]""";
        var (repaired, count) = HousingRateDefaults.ReplaceUnreviewed(json);
        Assert.Equal(1, count);
        var expected = JsonNode.Parse(json)!; expected[0]!["Occupants"]![0]!["Rate"] = "Standard";
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(repaired)));
        Assert.Equal((repaired, 0), HousingRateDefaults.ReplaceUnreviewed(repaired));
    }
}
