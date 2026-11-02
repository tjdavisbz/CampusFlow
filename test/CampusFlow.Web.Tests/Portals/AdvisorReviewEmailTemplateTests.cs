using CampusFlow.AdvisorPortal;
using CampusFlow.Web.Portals;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class AdvisorReviewEmailTemplateTests
{
    [Fact]
    public void Renders_All_Outcomes_And_Notes_Without_Html_Injection()
    {
        var review = new AdvisorReviewEmailDto
        {
            StudentName = "Student <name>",
            TermName = "Spring 2027",
            OverallComment = "Please check <details>.",
            Courses =
            [
                new() { CourseCode = "ENG-101", Section = "01", CourseName = "Writing", Status = "Approved" },
                new() { CourseCode = "COU-200", Section = "02", CourseName = "Counseling", Status = "Rejected" },
                new() { CourseCode = "PSY-200", Section = "03", CourseName = "Psychology", Status = "Still pending" }
            ]
        };

        var html = AdvisorReviewEmailTemplate.Render(review);

        html.ShouldContain("Approved courses");
        html.ShouldContain("Rejected courses");
        html.ShouldContain("Still pending review");
        html.ShouldContain("Student &lt;name&gt;");
        html.ShouldNotContain("Student <name>");
        html.ShouldContain("Please check &lt;details&gt;.");
        html.IndexOf("Advisor notes", System.StringComparison.Ordinal)
            .ShouldBeGreaterThan(html.IndexOf("Still pending review", System.StringComparison.Ordinal));
    }
}
