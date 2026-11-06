using CampusFlow.CourseSelections;
using Xunit;
namespace CampusFlow.Pages;
public class AcademicDisplayTests
{
    [Fact] public void Approval_is_a_display_label_and_does_not_change_the_Elements_status()
    {
        var row=new CourseSelectionRegistrationDto{Status="Unofficial",IsApprovedByScheduler=true};
        Assert.Equal("Approved",row.DisplayStatus);Assert.Equal("Unofficial",row.Status);
        row.Status="Official"; Assert.Equal("Official",row.DisplayStatus);
        row.Status="Withdrawn"; Assert.Equal("Withdrawn",row.DisplayStatus);
        row.Status="Unofficial";
        row.IsApprovedByScheduler=false;Assert.Equal("Unofficial",row.DisplayStatus);
    }
}
