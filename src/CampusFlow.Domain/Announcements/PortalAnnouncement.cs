using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Announcements;

[Flags]
public enum AnnouncementAudience
{
    Student = 1,
    Faculty = 2
}

public class PortalAnnouncement : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string ContentHtml { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public AnnouncementAudience Audience { get; private set; }
    public string TermsJson { get; private set; } = "[]";
    public bool IsPublished { get; private set; }

    protected PortalAnnouncement() { }

    public PortalAnnouncement(Guid id, Guid? tenantId, string title, string contentHtml,
        DateTime startsAtUtc, DateTime endsAtUtc, AnnouncementAudience audience,
        IEnumerable<string> terms, bool isPublished) : base(id)
    {
        TenantId = tenantId;
        Update(title, contentHtml, startsAtUtc, endsAtUtc, audience, terms, isPublished);
    }

    public IReadOnlyList<string> Terms => JsonSerializer.Deserialize<string[]>(TermsJson) ?? [];

    public void Update(string title, string contentHtml, DateTime startsAtUtc, DateTime endsAtUtc,
        AnnouncementAudience audience, IEnumerable<string> terms, bool isPublished)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160) throw new ArgumentException("A title of at most 160 characters is required.");
        if (string.IsNullOrWhiteSpace(contentHtml)) throw new ArgumentException("Announcement content is required.");
        if (endsAtUtc <= startsAtUtc) throw new ArgumentException("The end must be after the start.");
        if (audience is < AnnouncementAudience.Student or > (AnnouncementAudience.Student | AnnouncementAudience.Faculty))
            throw new ArgumentException("Select at least one audience.");
        Title = title.Trim();
        ContentHtml = contentHtml;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Audience = audience;
        TermsJson = JsonSerializer.Serialize(terms.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        IsPublished = isPublished;
    }

    public bool IsVisible(DateTime utcNow, AnnouncementAudience audience, string? termName) =>
        IsPublished && StartsAtUtc <= utcNow && utcNow < EndsAtUtc &&
        (Audience & audience) != 0 &&
        (Terms.Count == 0 || termName is not null && Terms.Contains(termName, StringComparer.OrdinalIgnoreCase));
}
