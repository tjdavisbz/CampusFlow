using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CampusFlow.AdvisorPortal;
using CampusFlow.CourseSelections;

namespace CampusFlow.Web.Portals;

public static class AdvisorReviewSendGuard
{
    public static bool AlreadySent(SubmitAdvisorReviewInput input, CourseReviewSubmission? lastSent)
    {
        if (lastSent is null) return false;

        // The message field starts empty for each new review. An empty field does not
        // constitute a new message, even if the last email included a message.
        var message = Normalize(input.OverallComment);
        if (message is not null && message != Normalize(lastSent.OverallComment)) return false;

        List<DecisionSnapshot>? previous;
        try
        {
            previous = JsonSerializer.Deserialize<List<DecisionSnapshot>>(lastSent.DecisionsSnapshotJson);
        }
        catch (JsonException)
        {
            return false;
        }
        if (previous is null) return false;

        if (input.Courses.Select(course => course.ReviewId).Distinct().Count() != input.Courses.Count ||
            previous.Select(course => course.ReviewId).Distinct().Count() != previous.Count)
            return false;
        var current = input.Courses.ToDictionary(course => course.ReviewId);
        var prior = previous.ToDictionary(course => course.ReviewId);
        foreach (var course in current.Values)
        {
            if (!prior.TryGetValue(course.ReviewId, out var sent) ||
                !string.Equals(Normalize(course.Decision), Normalize(sent.Decision),
                    StringComparison.OrdinalIgnoreCase) ||
                Normalize(course.Comment) != Normalize(sent.Comment))
                return false;
        }

        // A previously pending course disappearing is also a changed review. Approved
        // and rejected courses normally disappear from the pending list after saving.
        return previous.All(course =>
            Normalize(course.Decision) is not null || current.ContainsKey(course.ReviewId));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class DecisionSnapshot
    {
        public Guid ReviewId { get; set; }
        public string? Decision { get; set; }
        public string? Comment { get; set; }
    }
}
