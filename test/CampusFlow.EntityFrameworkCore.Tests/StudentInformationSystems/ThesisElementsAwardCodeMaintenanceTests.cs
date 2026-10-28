using CampusFlow.StudentInformationSystems;
using Shouldly;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public class ThesisElementsAwardCodeMaintenanceTests
{
    [Theory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    [InlineData(" yes ", true)]
    [InlineData("", false)]
    public void ElementsYesNoFieldsAreReadCorrectly(string value, bool expected)
    {
        ThesisElementsAwardCodeMaintenance.IsYes(value).ShouldBe(expected);
    }

    [Fact]
    public void ElementsYesNoFieldsAreWrittenAsStrings()
    {
        ThesisElementsAwardCodeMaintenance.ToElementsYesNo(true).ShouldBe("Yes");
        ThesisElementsAwardCodeMaintenance.ToElementsYesNo(false).ShouldBe("No");
    }
}
