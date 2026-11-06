using CampusFlow.Chapel;
using Xunit;
namespace CampusFlow.Domain.Tests.Chapel;

public class ChapelDormTests
{
    [Theory]
    [InlineData("Kendrick Hall", "kendrick")]
    [InlineData(" Savell ", "savell")]
    [InlineData("Teeter Hall", "teeter")]
    [InlineData("Bridges Hall", "bridges")]
    [InlineData("Guynes Hall", "guynes")]
    [InlineData("Collins Hall", "collins")]
    [InlineData("Commuter", "off-campus")]
    [InlineData("Commuter Dorm", "off-campus")]
    public void Known_buildings_have_explicit_dorm_filters(string building, string dorm) => Assert.Equal(dorm, ChapelDorms.Classify([building], []));
    [Fact] public void Missing_assignment_is_not_evidence_of_off_campus() => Assert.Equal("unknown", ChapelDorms.Classify([], ["Resident"]));
    [Fact] public void Explicit_commuter_status_is_off_campus() => Assert.Equal("off-campus", ChapelDorms.Classify([], ["Commuter"]));
    [Fact] public void Actual_room_assignment_overrides_status_description() => Assert.Equal("bridges", ChapelDorms.Classify(["Bridges Hall"], ["Commuter"]));
    [Fact] public void Conflicting_assignments_require_review() => Assert.Equal("unknown", ChapelDorms.Classify(["Bridges Hall", "Teeter Hall"], []));
    [Fact] public void Multiple_commuter_rooms_still_mean_one_off_campus_group() => Assert.Equal("off-campus", ChapelDorms.Classify(["Commuter", "Commuter"], []));
    [Fact] public void Unrequested_housing_is_kept_separate() => Assert.Equal("other", ChapelDorms.Classify(["Regents Apartment"], []));
}
