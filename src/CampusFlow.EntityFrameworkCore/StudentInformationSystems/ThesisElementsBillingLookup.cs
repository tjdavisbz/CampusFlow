using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsBillingLookup : IStudentInformationSystemBillingLookup
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";

    private readonly IConfiguration _configuration;

    public ThesisElementsBillingLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider =>
        StudentInformationSystemProvider.ThesisElements;

    public async Task<IReadOnlyList<StudentBillingTransaction>> GetTransactionsAsync(
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
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        const string sql = """
            SELECT
                'Ledger' AS Location,
                BB.BillingID AS TransactionID,
                BB.TermCalendarID,
                TC.TextTerm,
                TC.Term,
                BB.TransDate,
                BB.[Description],
                BB.Debits,
                BB.Credits,
                BB.ShowAmount,
                TD.ReportFlag,
                CASE WHEN BB.Voided = 'Yes' OR BB.Reversing = 'Yes'
                    THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS TransVoided
            FROM Billing BB
            LEFT JOIN Transdoc TD ON BB.TransDOCID = TD.TransDocID
            LEFT JOIN TermCalendar TC ON BB.TermCalendarID = TC.TermCalendarID
            WHERE BB.OwnerUID = @StudentUID

            UNION ALL

            SELECT
                'Batch' AS Location,
                BB.BillingBatchID AS TransactionID,
                BB.TermCalendarID,
                TC.TextTerm,
                TC.Term,
                BB.TransDate,
                BB.[Description],
                BB.Debits,
                BB.Credits,
                BB.ShowAmount,
                TD.ReportFlag,
                CAST(0 AS bit) AS TransVoided
            FROM BillingBatch BB
            LEFT JOIN Transdoc TD ON BB.TransDOCID = TD.TransDocID
            LEFT JOIN TermCalendar TC ON BB.TermCalendarID = TC.TermCalendarID
            WHERE BB.OwnerUID = @StudentUID

            ORDER BY Term DESC, TransDate, TransactionID
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;

        var transactions = new List<StudentBillingTransaction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(10) || !string.Equals(reader.GetString(10).Trim(), "Yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isPending = reader.GetString(0) == "Batch";
            var transactionId = Convert.ToString(reader.GetValue(1))!;
            transactions.Add(new StudentBillingTransaction(
                StudentInformationSystemProvider.ThesisElements,
                $"{(isPending ? "Batch" : "Ledger")}:{transactionId}",
                reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                reader.IsDBNull(4) ? "Unassigned" : reader.GetString(4).Trim(),
                reader.IsDBNull(3) ? "Other activity" : reader.GetString(3).Trim(),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? "Billing transaction" : reader.GetString(6).Trim(),
                reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                reader.IsDBNull(8) ? 0m : reader.GetDecimal(8),
                reader.IsDBNull(9) ? 0m : reader.GetDecimal(9),
                isPending,
                reader.GetBoolean(11)));
        }

        return transactions;
    }
}
