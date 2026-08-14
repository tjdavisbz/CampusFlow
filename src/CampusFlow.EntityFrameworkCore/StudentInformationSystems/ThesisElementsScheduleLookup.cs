using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsScheduleLookup : IStudentInformationSystemScheduleLookup
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private readonly IConfiguration _configuration;

    public ThesisElementsScheduleLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider =>
        StudentInformationSystemProvider.ThesisElements;

    public async Task<IReadOnlyList<StudentCourseScheduleItem>> GetScheduleAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalStudentId);
        if (!int.TryParse(externalStudentId, out var studentUid))
        {
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));
        }

        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        }

        const string sql = """
            SELECT
                schedule.SRAcademicID,
                schedule.TermCalendarID,
                schedule.Term,
                schedule.TextTerm,
                schedule.Department,
                schedule.CourseID,
                schedule.CourseType,
                schedule.Section,
                schedule.CourseName,
                schedule.Credits,
                schedule.RegistrationStatus,
                schedule.StartDate,
                schedule.CompletionDate,
                LTRIM(RTRIM(CONCAT(
                    schedule.ProfFirstName,
                    CASE WHEN NULLIF(LTRIM(RTRIM(schedule.ProfMiddleName)), '') IS NULL THEN '' ELSE ' ' + LTRIM(RTRIM(schedule.ProfMiddleName)) END,
                    CASE WHEN NULLIF(LTRIM(RTRIM(schedule.ProfLastName)), '') IS NULL THEN '' ELSE ' ' + LTRIM(RTRIM(schedule.ProfLastName)) END))),
                schedule.OfferDays,
                schedule.OfferTimeFrom,
                schedule.OfferTimeTo,
                NULLIF(LTRIM(RTRIM(CONCAT(
                    schedule.RoomAbbreviation,
                    CASE WHEN NULLIF(LTRIM(RTRIM(schedule.RoomNumber)), '') IS NULL THEN '' ELSE ' ' + LTRIM(RTRIM(schedule.RoomNumber)) END))), ''),
                CASE WHEN academic.ShowGradeReport = 'Yes' THEN academic.NumberGradeMidTerm END,
                CASE WHEN academic.ShowGradeReport = 'Yes' THEN academic.NumberGradeFinal END,
                CASE WHEN academic.ShowGradeReport = 'Yes' THEN NULLIF(LTRIM(RTRIM(academic.Grade)), '') END
            FROM [dbo].[CAMS_StudentRegisterScheduleDetail_View] schedule
            INNER JOIN [dbo].[CAMS_SRAcademic_View] academic
                ON academic.SRAcademicID = schedule.SRAcademicID
            WHERE schedule.StudentUID = @StudentUID
            ORDER BY schedule.Term DESC, schedule.Department, schedule.CourseID, schedule.Section
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;

        var courses = new List<StudentCourseScheduleItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var meetingDays = reader.IsDBNull(14) ? null : reader.GetString(14).Trim();
            var hasScheduledMeeting = !string.IsNullOrWhiteSpace(meetingDays) &&
                                      !string.Equals(meetingDays, "N\\A", StringComparison.OrdinalIgnoreCase) &&
                                      !string.Equals(meetingDays, "N/A", StringComparison.OrdinalIgnoreCase);

            courses.Add(new StudentCourseScheduleItem(
                StudentInformationSystemProvider.ThesisElements,
                Convert.ToString(reader.GetValue(0))!,
                Convert.ToString(reader.GetValue(1))!,
                reader.GetString(2).Trim(),
                reader.GetString(3).Trim(),
                reader.GetString(4).Trim(),
                reader.GetString(5).Trim(),
                reader.GetString(6).Trim(),
                reader.GetString(7).Trim(),
                reader.GetString(8).Trim(),
                Convert.ToDecimal(reader.GetValue(9)),
                reader.GetString(10).Trim(),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                reader.IsDBNull(13) ? "Instructor to be announced" : reader.GetString(13).Trim(),
                hasScheduledMeeting ? meetingDays : null,
                !hasScheduledMeeting || reader.IsDBNull(15) ? null : reader.GetDateTime(15).TimeOfDay,
                !hasScheduledMeeting || reader.IsDBNull(16) ? null : reader.GetDateTime(16).TimeOfDay,
                reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.IsDBNull(20) ? null : reader.GetString(20).Trim()));
        }

        return courses;
    }
}
