using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsCourseSelectionLookup : IStudentInformationSystemCourseSelectionLookup
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private readonly IConfiguration _configuration;

    public ThesisElementsCourseSelectionLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<CourseSelectionContext?> GetContextAsync(
        string externalStudentId, string externalTermId, CancellationToken cancellationToken = default)
    {
        var (studentUid, termCalendarId) = ParseIds(externalStudentId, externalTermId);
        const string sql = """
            SELECT TOP (1)
                status.StudentUID,
                status.TermCalendarID,
                status.TextTerm,
                student.AttendanceType,
                status.MaxAllowedHours
            FROM [dbo].[CAMS_StudentStatus_View] status
            INNER JOIN [dbo].[CAMS_Student_View] student
                ON student.StudentUID = status.StudentUID
            WHERE status.StudentUID = @StudentUID
              AND status.TermCalendarID = @TermCalendarID
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql, studentUid, termCalendarId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new CourseSelectionContext(
            Provider,
            reader.GetInt32(0).ToString(),
            reader.GetInt32(1).ToString(),
            reader.GetString(2).Trim(),
            reader.GetString(3).Trim(),
            Convert.ToDecimal(reader.GetValue(4)));
    }

    public async Task<IReadOnlyList<CourseSelectionContext>> GetEligibleContextsAsync(
        string externalStudentId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));

        const string sql = """
            SELECT DISTINCT
                status.StudentUID,
                status.TermCalendarID,
                term.TextTerm,
                student.AttendanceType,
                status.MaxAllowedHours,
                term.TermStartDate
            FROM [dbo].[CAMS_StudentStatus_View] status
            INNER JOIN [dbo].[TermCalendar] term
                ON term.TermCalendarID = status.TermCalendarID
            INNER JOIN [dbo].[CAMS_Student_View] student
                ON student.StudentUID = status.StudentUID
            WHERE status.StudentUID = @StudentUID
              AND term.TermStartDate >= DATEADD(year, -2, CAST(GETDATE() AS date))
              AND term.TermStartDate < DATEADD(year, 3, CAST(GETDATE() AS date))
            ORDER BY term.TermStartDate DESC
            """;

        var results = new List<CourseSelectionContext>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CourseSelectionContext(Provider, reader.GetInt32(0).ToString(),
                reader.GetInt32(1).ToString(), reader.GetString(2).Trim(), reader.GetString(3).Trim(),
                Convert.ToDecimal(reader.GetValue(4))));
        }
        return results;
    }

    public async Task<IReadOnlyList<CourseSelectionOffering>> GetAvailableOfferingsAsync(
        string externalTermId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalTermId, out var termCalendarId))
            throw new ArgumentException("The Thesis Elements term identifier is invalid.", nameof(externalTermId));

        const string sql = """
            SELECT DISTINCT
                offer.SROfferID,
                offer.SRMasterID,
                offer.SemesterID,
                offer.TextTerm,
                offer.Department,
                offer.Course,
                offer.CourseType,
                CAST(offer.Section AS varchar(30)),
                offer.CourseName,
                offer.Credits,
                '',
                offer.MaximumEnroll,
                offer.CurrentEnroll,
                offer.TempEnrollment,
                NULLIF(LTRIM(RTRIM(CONCAT(faculty.FirstName, ' ', faculty.LastName))), ''),
                NULLIF(LTRIM(RTRIM(schedule.OfferDays)), ''),
                schedule.OfferTimeFrom,
                schedule.OfferTimeTo
            FROM [dbo].[CAMS_SROffer_View] offer
            LEFT JOIN [dbo].[Faculty] faculty
                ON faculty.FacultyID = offer.FacultyID
            OUTER APPLY
            (
                SELECT TOP (1) s.OfferDays, s.OfferTimeFrom, s.OfferTimeTo
                FROM [dbo].[SROfferSchedule] s
                WHERE s.SROfferID = offer.SROfferID
                ORDER BY s.SROfferScheduleID
            ) schedule
            WHERE offer.SemesterID = @TermCalendarID
              AND offer.SRMasterID IS NOT NULL
              AND offer.DisplayInPortal = 1
            ORDER BY offer.Department, offer.Course, CAST(offer.Section AS varchar(30))
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TermCalendarID", SqlDbType.Int).Value = termCalendarId;
        var offerings = new List<CourseSelectionOffering>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            offerings.Add(new CourseSelectionOffering(
                Provider,
                reader.GetInt32(0).ToString(),
                reader.GetInt32(1).ToString(),
                reader.GetInt32(2).ToString(),
                reader.GetString(3).Trim(),
                reader.GetString(4).Trim(),
                Convert.ToString(reader.GetValue(5))!.Trim(),
                reader.GetString(6).Trim(),
                reader.GetString(7).Trim(),
                reader.GetString(8).Trim(),
                Convert.ToDecimal(reader.GetValue(9)),
                reader.GetString(10).Trim(),
                Convert.ToInt32(reader.GetValue(11)),
                Convert.ToInt32(reader.GetValue(12)),
                Convert.ToInt32(reader.GetValue(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetDateTime(16).TimeOfDay,
                reader.IsDBNull(17) ? null : reader.GetDateTime(17).TimeOfDay));
        }
        return offerings;
    }

    public async Task<IReadOnlyList<CourseSelectionRegistration>> GetRegistrationsAsync(
        string externalStudentId, string externalTermId, CancellationToken cancellationToken = default)
    {
        var (studentUid, termCalendarId) = ParseIds(externalStudentId, externalTermId);
        const string sql = """
            SELECT
                academic.SRAcademicID,
                academic.SROfferID,
                academic.TermCalendarID,
                academic.Department,
                academic.CourseID,
                academic.CourseType,
                CAST(academic.Section AS varchar(30)),
                academic.CourseName,
                academic.Credits,
                academic.RegistrationStatus,
                academic.EffectiveAddDate,
                academic.EffectiveWithdrawDate
            FROM [dbo].[SRAcademic] academic
            WHERE academic.StudentUID = @StudentUID
              AND academic.TermCalendarID = @TermCalendarID
              AND academic.SROfferID <> 0
            ORDER BY academic.Department, academic.CourseID, academic.Section
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql, studentUid, termCalendarId);
        var registrations = new List<CourseSelectionRegistration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            registrations.Add(new CourseSelectionRegistration(
                Provider,
                reader.GetInt32(0).ToString(),
                reader.GetInt32(1).ToString(),
                reader.GetInt32(2).ToString(),
                reader.GetString(3).Trim(),
                Convert.ToString(reader.GetValue(4))!.Trim(),
                reader.GetString(5).Trim(),
                reader.GetString(6).Trim(),
                reader.GetString(7).Trim(),
                Convert.ToDecimal(reader.GetValue(8)),
                reader.GetString(9).Trim(),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11)));
        }
        return registrations;
    }

    public async Task<bool> HasNonWithdrawnCourseAttemptAsync(
        string externalStudentId, string department, string courseCode, string courseType,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));

        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM [dbo].[SRAcademic] academic
                WHERE academic.StudentUID = @StudentUID
                  AND LTRIM(RTRIM(academic.Department)) = @Department
                  AND LTRIM(RTRIM(CONVERT(varchar(30), academic.CourseID))) = @CourseCode
                  AND LTRIM(RTRIM(academic.CourseType)) = @CourseType
                  AND academic.EffectiveWithdrawDate IS NULL
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@Department", SqlDbType.VarChar, 30).Value = department.Trim();
        command.Parameters.Add("@CourseCode", SqlDbType.VarChar, 30).Value = courseCode.Trim();
        command.Parameters.Add("@CourseType", SqlDbType.VarChar, 30).Value = courseType.Trim();
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<StudentCourseAttempt>> GetCourseAttemptsAsync(
        string externalStudentId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid))
            throw new ArgumentException("The Thesis Elements student identifier is invalid.", nameof(externalStudentId));

        const string sql = """
            WITH RankedAttempts AS
            (
                SELECT
                    LTRIM(RTRIM(academic.Department)) AS Department,
                    LTRIM(RTRIM(CONVERT(varchar(30), academic.CourseID))) AS CourseCode,
                    LTRIM(RTRIM(academic.CourseType)) AS CourseType,
                    LTRIM(RTRIM(COALESCE(academic.Grade, ''))) AS Grade,
                    CASE WHEN academic.EffectiveWithdrawDate IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS WasWithdrawn,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY academic.Department, academic.CourseID, academic.CourseType
                        ORDER BY COALESCE(academic.EffectiveWithdrawDate, academic.EffectiveAddDate) DESC,
                                 academic.TermCalendarID DESC,
                                 academic.SRAcademicID DESC
                    ) AS AttemptRank
                FROM dbo.SRAcademic academic
                WHERE academic.StudentUID = @StudentUID
                  AND academic.SROfferID <> 0
            )
            SELECT Department, CourseCode, CourseType, Grade, WasWithdrawn
            FROM RankedAttempts
            WHERE AttemptRank = 1
            """;

        var attempts = new List<StudentCourseAttempt>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(new StudentCourseAttempt(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
        }

        return attempts;
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string sql, int studentUid, int termCalendarId)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@TermCalendarID", SqlDbType.Int).Value = termCalendarId;
        return command;
    }

    private static (int StudentUid, int TermCalendarId) ParseIds(string externalStudentId, string externalTermId)
    {
        if (!int.TryParse(externalStudentId, out var studentUid) ||
            !int.TryParse(externalTermId, out var termCalendarId))
            throw new ArgumentException("The Thesis Elements student or term identifier is invalid.");
        return (studentUid, termCalendarId);
    }
}
