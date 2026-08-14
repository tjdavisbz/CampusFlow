using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsPaymentPlanLookup : IStudentInformationSystemPaymentPlanLookup
{
    private readonly IConfiguration _configuration;
    public ThesisElementsPaymentPlanLookup(IConfiguration configuration) => _configuration = configuration;
    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<StudentPaymentPlanContext?> GetPaymentPlanContextAsync(
        string externalStudentId, string externalTermId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalStudentId, out var studentUid) || !int.TryParse(externalTermId, out var termId))
            throw new ArgumentException("The Thesis Elements identifiers are invalid.");
        var connectionString = _configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Thesis Elements read-only connection is not configured.");

        const string sql = """
            SELECT
                ISNULL(FTPT.DisplayText, '') AS FTPTStatus,
                ISNULL(Attendance.DisplayText, '') AS AttendanceType,
                CASE WHEN Resident.DisplayText = 'Commuter' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Commuter
            FROM Student S
            LEFT JOIN StudentStatus SS ON SS.StudentUID = S.StudentUID AND SS.TermCalendarID = @TermCalendarID
            LEFT JOIN Glossary FTPT ON FTPT.UniqueID = SS.FTPTStatusID
            LEFT JOIN Glossary Attendance ON Attendance.UniqueID = S.AttendanceTypeID
            LEFT JOIN Glossary Resident ON Resident.UniqueID = SS.ResidentCommuterID
            WHERE S.StudentUID = @StudentUID
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@TermCalendarID", SqlDbType.Int).Value = termId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StudentPaymentPlanContext(reader.GetString(0).Trim(), reader.GetString(1).Trim(), reader.GetBoolean(2));
    }
}
