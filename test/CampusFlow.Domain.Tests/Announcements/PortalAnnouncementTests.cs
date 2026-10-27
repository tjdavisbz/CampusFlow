using System;
using Shouldly;
using Xunit;

namespace CampusFlow.Announcements;

public class PortalAnnouncementTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2027, 1, 20, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Specific_terms_and_audiences_are_enforced()
    {
        var announcement = Create(["Spring 2027"], AnnouncementAudience.Student | AnnouncementAudience.Faculty);
        announcement.IsVisible(Start, AnnouncementAudience.Student, "Spring 2027").ShouldBeTrue();
        announcement.IsVisible(Start, AnnouncementAudience.Faculty, "spring 2027").ShouldBeTrue();
        announcement.IsVisible(Start, AnnouncementAudience.Student, "Fall 2026").ShouldBeFalse();
        announcement.IsVisible(End, AnnouncementAudience.Student, "Spring 2027").ShouldBeFalse();
    }

    [Fact]
    public void All_terms_and_publish_state_are_enforced()
    {
        var announcement = Create([], AnnouncementAudience.Student);
        announcement.IsVisible(Start, AnnouncementAudience.Student, "Fall 2026").ShouldBeTrue();
        announcement.IsVisible(Start, AnnouncementAudience.Faculty, "Fall 2026").ShouldBeFalse();
        announcement.Update("Title", "<p>Text</p>", Start, End, AnnouncementAudience.Student, [], false);
        announcement.IsVisible(Start, AnnouncementAudience.Student, "Fall 2026").ShouldBeFalse();
    }

    private static PortalAnnouncement Create(string[] terms, AnnouncementAudience audience) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Title", "<p>Text</p>", Start, End, audience, terms, true);
}
