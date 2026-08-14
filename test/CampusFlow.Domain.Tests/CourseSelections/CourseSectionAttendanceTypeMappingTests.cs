using System;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public class CourseSectionAttendanceTypeMappingTests
{
    [Fact]
    public void Includes_section_within_active_effective_range()
    {
        var mapping = new CourseSectionAttendanceTypeMapping(Guid.NewGuid(), Guid.NewGuid(),
            200, 240, 3421, "Distance Education", new DateTime(2026, 1, 1));

        mapping.Includes(220, new DateTime(2026, 8, 14)).ShouldBeTrue();
        mapping.Includes(241, new DateTime(2026, 8, 14)).ShouldBeFalse();
        mapping.Includes(220, new DateTime(2025, 12, 31)).ShouldBeFalse();
    }
}
