using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
namespace CampusFlow.StudentInformationSystems;

public sealed record CourseMeeting(string? Days, TimeSpan? StartTime, TimeSpan? EndTime, string? Room = null)
{
    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var days = CleanDays(Days);
            if (StartTime is null && EndTime is null)
                return days is null ? "No scheduled meeting time" : $"{days} · Time to be announced";
            var time = $"{FormatTime(StartTime)}–{FormatTime(EndTime)}";
            return days is null ? time : $"{days} · {time}";
        }
    }
    private static string FormatTime(TimeSpan? time) => time is null ? "TBA" :
        new DateTime(2000, 1, 1).Add(time.Value).ToString("h:mm tt", CultureInfo.InvariantCulture);
    private static string? CleanDays(string? days) => string.IsNullOrWhiteSpace(days) ||
        days.Trim().ToUpperInvariant() is "N/A" or "N\\A" ? null : days.Trim();
    public static IReadOnlyList<CourseMeeting> Resolve(IReadOnlyList<CourseMeeting>? meetings,
        string? days, TimeSpan? start, TimeSpan? end, string? room = null)
    {
        IEnumerable<CourseMeeting> rows = meetings?.Count > 0 ? meetings : [new(days, start, end, room)];
        return rows.Select(x => x with { Days = CleanDays(x.Days), Room = x.Room?.Trim() })
            .Where(x => x.Days is not null || x.StartTime is not null || x.EndTime is not null)
            .Distinct().OrderBy(x => x.StartTime).ThenBy(x => x.Days, StringComparer.Ordinal).ToArray();
    }
}
