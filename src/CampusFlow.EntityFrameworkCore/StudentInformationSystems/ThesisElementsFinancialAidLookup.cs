using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsFinancialAidLookup : IStudentInformationSystemFinancialAidLookup
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private readonly IConfiguration _configuration;

    public ThesisElementsFinancialAidLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider =>
        StudentInformationSystemProvider.ThesisElements;

    public async Task<IReadOnlyList<StudentFinancialAidAward>> GetAwardsAsync(
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
                FinancialAwardID,
                TermCalendarID,
                Term,
                TextTerm,
                TransDate,
                AwardType,
                AwardStatus,
                [Description],
                Amount,
                SentToBilling,
                StudentAccepted,
                StudentAcceptedTime
            FROM [dbo].[CAMS_FinancialAward_View]
            WHERE StudentUID = @StudentUID
            ORDER BY Term DESC, TransDate DESC, FinancialAwardID DESC
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;

        var awards = new List<StudentFinancialAidAward>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            awards.Add(new StudentFinancialAidAward(
                StudentInformationSystemProvider.ThesisElements,
                Convert.ToString(reader.GetValue(0))!,
                Convert.ToString(reader.GetValue(1))!,
                reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2))!.Trim(),
                reader.IsDBNull(3) ? "Other term" : Convert.ToString(reader.GetValue(3))!.Trim(),
                reader.IsDBNull(4) ? null : Convert.ToDateTime(reader.GetValue(4)),
                reader.IsDBNull(5) ? "Award" : Convert.ToString(reader.GetValue(5))!.Trim(),
                reader.IsDBNull(6) ? "Status unavailable" : Convert.ToString(reader.GetValue(6))!.Trim(),
                reader.IsDBNull(7) ? "Financial aid award" : Convert.ToString(reader.GetValue(7))!.Trim(),
                reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetValue(8)),
                ReadBoolean(reader, 9),
                reader.IsDBNull(10) ? null : Convert.ToInt32(reader.GetValue(10)),
                reader.IsDBNull(11) ? null : Convert.ToDateTime(reader.GetValue(11))));
        }

        return awards;
    }

    private static bool ReadBoolean(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var value = reader.GetValue(ordinal);
        return value is bool boolean
            ? boolean
            : string.Equals(Convert.ToString(value)?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(Convert.ToString(value)?.Trim(), "True", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(Convert.ToString(value)?.Trim(), "1", StringComparison.OrdinalIgnoreCase);
    }
}
