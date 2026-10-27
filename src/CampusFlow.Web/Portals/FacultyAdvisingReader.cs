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

public sealed record FacultyAdvisee(int StudentUid, string StudentId, string Name, string Programs);

/// <summary>
/// Reads advisees assigned through Elements student programs. Faculty identity must first be
/// established through FacultyPortalReader; every query then scopes advisor IDs to that faculty ID.
/// </summary>
public sealed class FacultyAdvisingReader(IConfiguration configuration) : ITransientDependency
{
    private const string AdvisorScope = """
        WITH FacultyAdvisors AS (
            SELECT DISTINCT u.AdvisorID
            FROM dbo.CAMSUser u
            INNER JOIN dbo.Advisors a ON a.AdvisorID = u.AdvisorID AND a.ActiveFlag = 1
            WHERE u.FacultyID = @FacultyID AND u.DisableLogin = 0
        )
        """;

    public async Task<bool> HasAdvisorAccessAsync(int facultyId, CancellationToken cancellationToken)
    {
        const string sql = AdvisorScope + """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM FacultyAdvisors) THEN 1 ELSE 0 END
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(sql, connection, facultyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<FacultyTerm>> GetTermsAsync(int facultyId,
        CancellationToken cancellationToken)
    {
        const string sql = AdvisorScope + """
            SELECT TOP (24) t.TermCalendarID, LTRIM(RTRIM(t.TextTerm)),
                t.TermStartDate, t.TermEndDate
            FROM dbo.TermCalendar t
            WHERE t.DisplayInFacultyPortal = 1
               OR EXISTS (
                    SELECT 1 FROM dbo.CAMS_StudentProgram_View sp
                    INNER JOIN FacultyAdvisors fa ON fa.AdvisorID = sp.AdvisorID
                    WHERE sp.TermCalendarID = t.TermCalendarID)
            ORDER BY t.TermStartDate DESC, t.TermCalendarID DESC
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(sql, connection, facultyId);
        var terms = new List<FacultyTerm>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            terms.Add(new FacultyTerm(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        return terms;
    }

    public async Task<IReadOnlyList<FacultyAdvisee>> GetAdviseesAsync(int facultyId, int termId,
        CancellationToken cancellationToken)
    {
        const string sql = AdvisorScope + """
            SELECT DISTINCT st.StudentUID, LTRIM(RTRIM(st.StudentID)),
                LTRIM(RTRIM(CONCAT(st.LastName, ', ', st.FirstName))),
                COALESCE(NULLIF(LTRIM(RTRIM(sp.BlockProgram)), ''),
                         NULLIF(LTRIM(RTRIM(sp.MajorDegree)), ''), '')
            FROM dbo.CAMS_StudentProgram_View sp
            INNER JOIN FacultyAdvisors fa ON fa.AdvisorID = sp.AdvisorID
            INNER JOIN dbo.Student st ON st.StudentUID = sp.StudentUID
            WHERE sp.TermCalendarID = @TermID
            ORDER BY 3, 2, 4, st.StudentUID
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(sql, connection, facultyId);
        command.Parameters.Add("@TermID", SqlDbType.Int).Value = termId;
        var rows = new List<(int Uid, string Id, string Name, string Program)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));

        return rows.GroupBy(x => x.Uid)
            .Select(group => new FacultyAdvisee(group.Key, group.First().Id, group.First().Name,
                string.Join(", ", group.Select(x => x.Program)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase))))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Elements read-only connection is not configured.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static SqlCommand Command(string sql, SqlConnection connection, int facultyId)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@FacultyID", SqlDbType.Int).Value = facultyId;
        return command;
    }
}
