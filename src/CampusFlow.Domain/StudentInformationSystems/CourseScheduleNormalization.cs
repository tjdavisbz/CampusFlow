using System;
using System.Collections.Generic;
using System.Linq;
namespace CampusFlow.StudentInformationSystems;

public static class CourseScheduleNormalization
{
    public static IReadOnlyList<StudentCourseScheduleItem> Registrations(IEnumerable<StudentCourseScheduleItem> rows) =>
        rows.Select((row, index) => (row, index)).GroupBy(x =>
            (x.row.Provider, x.row.ExternalTermId, Identity(x.row.ExternalAcademicId, x.index)))
        .Select(group =>
        {
            var first = group.First().row;
            if (group.Select(x => (x.row.ExternalCourseOfferingId, x.row.Credits)).Distinct().Count() != 1)
                throw new InvalidOperationException("An enrolled course has conflicting registration details.");
            return first with
            {
                Instructor = Instructors(group.Select(x => x.row.Instructor)),
                Meetings = group.SelectMany(x => CourseMeeting.Resolve(x.row.Meetings,
                    x.row.MeetingDays, x.row.StartTime, x.row.EndTime, x.row.Room)).Distinct().ToArray()
            };
        }).ToArray();

    public static IReadOnlyList<CourseSelectionOffering> Offerings(IEnumerable<CourseSelectionOffering> rows) =>
        rows.Select((row, index) => (row, index)).GroupBy(x =>
            (x.row.Provider, x.row.ExternalTermId, Identity(x.row.ExternalOfferingId, x.index)))
        .Select(group =>
        {
            var first = group.First().row;
            if (group.Select(x => (x.row.ExternalMasterCourseId, x.row.Credits)).Distinct().Count() != 1)
                throw new InvalidOperationException("A course offering has conflicting details.");
            return first with
            {
                InstructorName = Instructors(group.Select(x => x.row.InstructorName)),
                Meetings = group.SelectMany(x => CourseMeeting.Resolve(x.row.Meetings,
                    x.row.MeetingDays, x.row.StartTime, x.row.EndTime)).Distinct().ToArray()
            };
        }).ToArray();

    private static string Identity(string? id, int index) =>
        string.IsNullOrWhiteSpace(id) ? $"missing:{index}" : $"id:{id}";
    private static string Instructors(IEnumerable<string?> names)
    {
        var known = names.Where(x => !string.IsNullOrWhiteSpace(x) &&
            !string.Equals(x.Trim(), "Instructor to be announced", StringComparison.OrdinalIgnoreCase))
            .Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return known.Length == 0 ? "Instructor to be announced" : string.Join(", ", known);
    }
}
