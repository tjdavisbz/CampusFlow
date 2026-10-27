using CampusFlow.Web.Announcements;
using Shouldly;
using Xunit;

namespace CampusFlow.Announcements;

public class AnnouncementHtmlTests
{
    [Fact]
    public void Preserves_basic_formatting_and_safe_links()
    {
        var html = AnnouncementHtml.Sanitize("<p><strong>Spring</strong> <a href=\"https://drive.google.com/file\">Schedule</a></p>");
        html.ShouldContain("<strong>Spring</strong>");
        html.ShouldContain("href=\"https://drive.google.com/file\"");
        html.ShouldContain("rel=\"noopener noreferrer\"");
    }

    [Fact]
    public void Removes_active_content_and_unsafe_urls()
    {
        var html = AnnouncementHtml.Sanitize("<script>alert(1)</script><p onclick=\"x()\">News</p><a href=\"javascript:alert(2)\">bad</a><img src=x onerror=\"x()\">");
        html.ShouldContain("<p>News</p>");
        html.ShouldNotContain("script");
        html.ShouldNotContain("onclick");
        html.ShouldNotContain("javascript:");
        html.ShouldNotContain("<img");
    }
}
