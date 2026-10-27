using System;
using CampusFlow.Web.Pages.Faculty;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class FacultyScheduleFormattingTests
{
    [Theory]
    [InlineData(0, 0, "12:00 AM")]
    [InlineData(9, 5, "9:05 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(13, 30, "1:30 PM")]
    public void Schedule_Time_Uses_12_Hour_Clock(int hour, int minute, string expected)
    {
        IndexModel.FormatTime(new TimeSpan(hour, minute, 0)).ShouldBe(expected);
    }

    [Fact]
    public void Missing_Schedule_Time_Uses_Dash()
    {
        IndexModel.FormatTime(null).ShouldBe("—");
    }
}
