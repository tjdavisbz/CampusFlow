using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Web.Portals;

public sealed record FacultyIdentity(int FacultyId, string DisplayName);
public sealed record FacultySearchResult(int FacultyId, string DisplayName, string? Email, string? PortalAlias);
public sealed record FacultyTerm(int Id, string Name, DateTime? StartsAt, DateTime? EndsAt);
public sealed record FacultyCourse(int OfferId, string Code, string Name, string? CourseType,
    string? Section, int StudentCount, DateTime? StartsAt, DateTime? EndsAt);
public sealed record FacultyMeeting(int OfferId, string CourseCode, string? Section, string? Days,
    TimeSpan? StartsAt, TimeSpan? EndsAt);
public sealed record FacultyStudent(int StudentUid, string StudentId, string Name,
    int OfferId, string CourseCode, string? Section, string RegistrationStatus);
public sealed record FacultyEmailRecipient(int StudentUid, string Name, string? Email);

/// <summary>Read-only faculty data. Every query is bound to a faculty ID proven by FacultyPortal membership.</summary>
public sealed class FacultyPortalReader(IConfiguration configuration) : ITransientDependency
{
    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Elements read-only connection is not configured.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> HasStaffOrFacultyIdentityAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.CAMSUser u
                WHERE u.DisableLogin = 0 AND LOWER(LTRIM(RTRIM(u.EmailAddress))) = LOWER(@Email))
              OR EXISTS (
                SELECT 1 FROM dbo.FacultyPortal fp
                INNER JOIN dbo.CAMS_FacultyAddressList_View a ON a.FacultyID = fp.FacultyID
                WHERE a.ActiveFlag = 'Yes' AND LOWER(LTRIM(RTRIM(a.Email1))) = LOWER(@Email))
            THEN 1 ELSE 0 END
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Email", SqlDbType.VarChar, 256).Value = email.Trim();
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    public async Task<FacultyIdentity?> FindFacultyAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        const string sql = """
            SELECT DISTINCT TOP (2) fp.FacultyID,
                LTRIM(RTRIM(CONCAT(f.FirstName, ' ', f.LastName)))
            FROM dbo.FacultyPortal fp
            INNER JOIN dbo.Faculty f ON f.FacultyID = fp.FacultyID
            WHERE EXISTS (
                SELECT 1 FROM dbo.CAMSUser u
                WHERE u.FacultyID = fp.FacultyID AND u.DisableLogin = 0
                  AND LOWER(LTRIM(RTRIM(u.EmailAddress))) = LOWER(@Email))
               OR EXISTS (
                SELECT 1 FROM dbo.CAMS_FacultyAddressList_View a
                WHERE a.FacultyID = fp.FacultyID AND a.ActiveFlag = 'Yes'
                  AND LOWER(LTRIM(RTRIM(a.Email1))) = LOWER(@Email))
            ORDER BY fp.FacultyID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Email", SqlDbType.VarChar, 256).Value = email.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var result = new FacultyIdentity(reader.GetInt32(0), reader.GetString(1));
        if (await reader.ReadAsync(cancellationToken)) return null;
        return result;
    }

    public async Task<FacultyIdentity?> FindByIdAsync(int facultyId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) fp.FacultyID,
                LTRIM(RTRIM(CONCAT(f.FirstName, ' ', f.LastName)))
            FROM dbo.FacultyPortal fp
            INNER JOIN dbo.Faculty f ON f.FacultyID = fp.FacultyID
            WHERE fp.FacultyID = @FacultyID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FacultyID", SqlDbType.Int).Value = facultyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FacultyIdentity(reader.GetInt32(0), reader.GetString(1))
            : null;
    }

    public async Task<IReadOnlyList<FacultySearchResult>> SearchAsync(string search,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2) return [];
        const string sql = """
            SELECT TOP (25) fp.FacultyID,
                LTRIM(RTRIM(CONCAT(f.FirstName, ' ', f.LastName))),
                COALESCE(contact.Email1, userContact.EmailAddress), fp.PortalAlias
            FROM dbo.FacultyPortal fp
            INNER JOIN dbo.Faculty f ON f.FacultyID = fp.FacultyID
            OUTER APPLY (
                SELECT TOP (1) a.Email1
                FROM dbo.CAMS_FacultyAddressList_View a
                WHERE a.FacultyID = fp.FacultyID AND a.ActiveFlag = 'Yes'
                  AND NULLIF(LTRIM(RTRIM(a.Email1)), '') IS NOT NULL
                ORDER BY CASE WHEN a.AddressType = 'Home' THEN 0 ELSE 1 END, a.AddressID
            ) contact
            OUTER APPLY (
                SELECT TOP (1) u.EmailAddress
                FROM dbo.CAMSUser u
                WHERE u.FacultyID = fp.FacultyID AND u.DisableLogin = 0
                  AND NULLIF(LTRIM(RTRIM(u.EmailAddress)), '') IS NOT NULL
                ORDER BY u.CAMSUserID
            ) userContact
            WHERE CONVERT(varchar(20), fp.FacultyID) = @ExactSearch
               OR LOWER(LTRIM(RTRIM(CONCAT(f.FirstName, ' ', f.LastName)))) LIKE @Search
               OR LOWER(LTRIM(RTRIM(CONCAT(f.LastName, ', ', f.FirstName)))) LIKE @Search
               OR LOWER(LTRIM(RTRIM(fp.PortalAlias))) LIKE @Search
               OR EXISTS (
                    SELECT 1 FROM dbo.CAMS_FacultyAddressList_View a
                    WHERE a.FacultyID = fp.FacultyID
                      AND LOWER(LTRIM(RTRIM(a.Email1))) LIKE @Search)
               OR EXISTS (
                    SELECT 1 FROM dbo.CAMSUser u
                    WHERE u.FacultyID = fp.FacultyID AND u.DisableLogin = 0
                      AND LOWER(LTRIM(RTRIM(u.EmailAddress))) LIKE @Search)
            ORDER BY f.LastName, f.FirstName, fp.FacultyID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ExactSearch", SqlDbType.VarChar, 80).Value = search.Trim();
        command.Parameters.Add("@Search", SqlDbType.VarChar, 260).Value = $"%{search.Trim().ToLowerInvariant()}%";
        var result = new List<FacultySearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultySearchResult(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                reader.IsDBNull(3) ? null : reader.GetString(3).Trim()));
        return result;
    }

    public async Task<IReadOnlyList<FacultyTerm>> GetTermsAsync(int facultyId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (24) t.TermCalendarID, LTRIM(RTRIM(t.TextTerm)),
                t.TermStartDate, t.TermEndDate
            FROM dbo.TermCalendar t
            WHERE t.DisplayInFacultyPortal = 1
               OR EXISTS (SELECT 1 FROM dbo.SROffer o WHERE o.TermCalendarID = t.TermCalendarID
                    AND (o.FacultyID = @FacultyID OR o.FacultyID2 = @FacultyID
                         OR EXISTS (SELECT 1 FROM dbo.SROfferSchedule s
                             INNER JOIN dbo.SROfferSchedule_Faculty sf
                                 ON sf.SROfferScheduleID = s.SROfferScheduleID
                             WHERE s.SROfferID = o.SROfferID AND sf.FacultyID = @FacultyID)
                         OR EXISTS (SELECT 1 FROM dbo.CourseManagementMenuAccess c
                             WHERE c.SROfferID = o.SROfferID AND c.FacultyID = @FacultyID)))
            ORDER BY t.TermStartDate DESC, t.TermCalendarID DESC
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FacultyID", SqlDbType.Int).Value = facultyId;
        var result = new List<FacultyTerm>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultyTerm(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        return result;
    }

    private const string FacultyCourses = """
        WITH FacultyCourses AS (
            SELECT o.SROfferID, o.Department, o.CourseID, o.CourseType, o.Section,
                o.Coursename, o.StartDate, o.EndDate
            FROM dbo.SROffer o
            WHERE o.TermCalendarID = @TermID
              AND (o.FacultyID = @FacultyID OR o.FacultyID2 = @FacultyID
                OR EXISTS (SELECT 1 FROM dbo.SROfferSchedule s
                    INNER JOIN dbo.SROfferSchedule_Faculty sf
                        ON sf.SROfferScheduleID = s.SROfferScheduleID
                    WHERE s.SROfferID = o.SROfferID AND sf.FacultyID = @FacultyID)
                OR EXISTS (SELECT 1 FROM dbo.CourseManagementMenuAccess c
                    WHERE c.SROfferID = o.SROfferID AND c.FacultyID = @FacultyID))
        )
        """;

    public async Task<IReadOnlyList<FacultyCourse>> GetCoursesAsync(int facultyId, int termId,
        CancellationToken cancellationToken)
    {
        const string sql = FacultyCourses + """
            SELECT fc.SROfferID, LTRIM(RTRIM(CONCAT(fc.Department, ' ', fc.CourseID))),
                LTRIM(RTRIM(fc.Coursename)), fc.CourseType, fc.Section,
                (SELECT COUNT(DISTINCT a.StudentUID) FROM dbo.SRAcademic a
                    WHERE a.SROfferID = fc.SROfferID
                      AND a.RegistrationStatus IN ('Official', 'Unofficial')),
                fc.StartDate, fc.EndDate
            FROM FacultyCourses fc
            ORDER BY fc.Department, fc.CourseID, fc.Section, fc.SROfferID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateScopedCommand(sql, connection, facultyId, termId);
        var result = new List<FacultyCourse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultyCourse(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7)));
        return result;
    }

    public async Task<IReadOnlyList<FacultyMeeting>> GetMeetingsAsync(int facultyId, int termId,
        CancellationToken cancellationToken)
    {
        const string sql = FacultyCourses + """
            SELECT fc.SROfferID, LTRIM(RTRIM(CONCAT(fc.Department, ' ', fc.CourseID))),
                fc.Section, s.OfferDays, s.OfferTimeFrom, s.OfferTimeTo
            FROM FacultyCourses fc
            INNER JOIN dbo.SROfferSchedule s ON s.SROfferID = fc.SROfferID
            ORDER BY s.OfferDays, s.OfferTimeFrom, fc.Department, fc.CourseID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateScopedCommand(sql, connection, facultyId, termId);
        var result = new List<FacultyMeeting>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultyMeeting(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4).TimeOfDay,
                reader.IsDBNull(5) ? null : reader.GetDateTime(5).TimeOfDay));
        return result;
    }

    public async Task<IReadOnlyList<FacultyStudent>> GetStudentsAsync(int facultyId, int termId,
        CancellationToken cancellationToken)
    {
        const string sql = FacultyCourses + """
            SELECT DISTINCT st.StudentUID, LTRIM(RTRIM(st.StudentID)),
                LTRIM(RTRIM(CONCAT(st.LastName, ', ', st.FirstName))),
                fc.SROfferID, LTRIM(RTRIM(CONCAT(fc.Department, ' ', fc.CourseID))),
                fc.Section, LTRIM(RTRIM(a.RegistrationStatus))
            FROM FacultyCourses fc
            INNER JOIN dbo.SRAcademic a ON a.SROfferID = fc.SROfferID
                AND a.TermCalendarID = @TermID
                AND a.RegistrationStatus IN ('Official', 'Unofficial')
            INNER JOIN dbo.Student st ON st.StudentUID = a.StudentUID
            ORDER BY 3, 5, st.StudentUID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateScopedCommand(sql, connection, facultyId, termId);
        var result = new List<FacultyStudent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultyStudent(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5).Trim(), reader.GetString(6)));
        return result;
    }

    public async Task<IReadOnlyList<FacultyEmailRecipient>> GetEmailRecipientsAsync(
        int facultyId, int termId, int offerId, CancellationToken cancellationToken)
    {
        const string sql = FacultyCourses + """
            SELECT DISTINCT st.StudentUID,
                LTRIM(RTRIM(CONCAT(st.LastName, ', ', st.FirstName))),
                LTRIM(RTRIM(address.Email1))
            FROM FacultyCourses fc
            INNER JOIN dbo.SRAcademic a ON a.SROfferID = fc.SROfferID
                AND a.TermCalendarID = @TermID
                AND a.RegistrationStatus IN ('Official', 'Unofficial')
            INNER JOIN dbo.Student st ON st.StudentUID = a.StudentUID
            OUTER APPLY (
                SELECT TOP (1) contact.Email1
                FROM dbo.CAMS_StudentAddressList_View contact
                WHERE contact.StudentUID = st.StudentUID
                  AND contact.ActiveFlag = 'Yes' AND contact.AddressType = 'Local'
                  AND NULLIF(LTRIM(RTRIM(contact.Email1)), '') IS NOT NULL
                ORDER BY contact.AddressID DESC
            ) address
            WHERE fc.SROfferID = @OfferID
            ORDER BY 2, st.StudentUID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateScopedCommand(sql, connection, facultyId, termId);
        command.Parameters.Add("@OfferID", SqlDbType.Int).Value = offerId;
        var result = new List<FacultyEmailRecipient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FacultyEmailRecipient(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }

    private static SqlCommand CreateScopedCommand(string sql, SqlConnection connection, int facultyId, int termId)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FacultyID", SqlDbType.Int).Value = facultyId;
        command.Parameters.Add("@TermID", SqlDbType.Int).Value = termId;
        return command;
    }
}
