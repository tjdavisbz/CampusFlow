using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelAttendanceSearchTests
{
    [Theory]
    [InlineData("Terry Davis", new[] { "Terry", "Davis" })]
    [InlineData("Davis, Terry", new[] { "Davis", "Terry" })]
    [InlineData("Ter Dav", new[] { "Ter", "Dav" })]
    public void Name_search_splits_independent_fragments(string input, string[] expected)
    {
        ChapelAttendanceAppService.SplitStudentSearch(input).ShouldBe(expected);
    }
}
