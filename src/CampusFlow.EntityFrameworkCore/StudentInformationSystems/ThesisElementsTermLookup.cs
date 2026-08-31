using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsTermLookup :
    IStudentInformationSystemTermLookup,
    ITransientDependency
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";

    private readonly IConfiguration _configuration;

    public ThesisElementsTermLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider =>
        StudentInformationSystemProvider.ThesisElements;

    public async Task<StudentInformationSystemTerm?> GetCurrentTermAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        var currentTermOverride = _configuration[
            "StudentInformationSystems:Providers:ThesisElements:CurrentTermOverride"];
        var sql = string.IsNullOrWhiteSpace(currentTermOverride)
            ? """
            SELECT TOP (1)
                TermCalendarID,
                Term,
                TextTerm,
                TermStartDate,
                TermEndDate
            FROM [dbo].[TermCalendar]
            WHERE TermStartDate <= @Today
            ORDER BY Term DESC
            """
            : """
            SELECT TOP (1)
                TermCalendarID,
                Term,
                TextTerm,
                TermStartDate,
                TermEndDate
            FROM [dbo].[TermCalendar]
            WHERE Term = @Term
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        if (string.IsNullOrWhiteSpace(currentTermOverride))
        {
            command.Parameters.Add("@Today", SqlDbType.DateTime2).Value = DateTime.Today;
        }
        else
        {
            command.Parameters.Add("@Term", SqlDbType.VarChar, 20).Value = currentTermOverride.Trim();
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StudentInformationSystemTerm(
            StudentInformationSystemProvider.ThesisElements,
            Convert.ToString(reader.GetValue(0))!,
            reader.GetString(1).Trim(),
            reader.GetString(2).Trim(),
            reader.GetDateTime(3),
            reader.GetDateTime(4));
    }

    public async Task<IReadOnlyList<StudentInformationSystemTerm>> GetTermsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        const string sql = """
            SELECT TermCalendarID, Term, TextTerm, TermStartDate, TermEndDate
            FROM dbo.TermCalendar
            WHERE TermStartDate >= DATEADD(year, -2, CAST(GETDATE() AS date))
              AND TermStartDate < DATEADD(year, 4, CAST(GETDATE() AS date))
            ORDER BY TermStartDate DESC
            """;
        var terms = new List<StudentInformationSystemTerm>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            terms.Add(new StudentInformationSystemTerm(Provider, Convert.ToString(reader.GetValue(0))!,
                reader.GetString(1).Trim(), reader.GetString(2).Trim(), reader.GetDateTime(3), reader.GetDateTime(4)));
        return terms;
    }
}
