using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsStudentLookup :
    IStudentInformationSystemStudentLookup,
    ITransientDependency
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";

    private readonly IConfiguration _configuration;

    public ThesisElementsStudentLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public StudentInformationSystemProvider Provider =>
        StudentInformationSystemProvider.ThesisElements;

    public async Task<StudentLookupResult> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        const string sql = """
            SELECT TOP (2)
                student.StudentUID, student.StudentID, address.Email1,
                student.FirstName, student.PreferredName, student.LastName
            FROM dbo.CAMS_StudentAddressList_View address
            INNER JOIN dbo.CAMS_Student_View student ON student.StudentUID = address.StudentUID
            WHERE address.ActiveFlag = @ActiveFlag
              AND address.AddressType = @AddressType
              AND LTRIM(RTRIM(address.Email1)) = @Email
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddAddressParameters(command);
        command.Parameters.Add("@Email", SqlDbType.VarChar, 320).Value = email.Trim();

        var matches = await ReadStudentsAsync(command, cancellationToken);
        return matches.Count switch
        {
            0 => StudentLookupResult.NotFound(),
            1 => StudentLookupResult.Matched(matches[0]),
            _ => StudentLookupResult.Ambiguous()
        };
    }

    public async Task<StudentInformationSystemStudent?> FindByExternalStudentIdAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalStudentId);
        if (!int.TryParse(externalStudentId.Trim(), out var studentUid)) return null;

        const string sql = """
            SELECT TOP (1)
                student.StudentUID, student.StudentID, COALESCE(address.Email1, ''),
                student.FirstName, student.PreferredName, student.LastName
            FROM dbo.CAMS_Student_View student
            OUTER APPLY (
                SELECT TOP (1) localAddress.Email1
                FROM dbo.CAMS_StudentAddressList_View localAddress
                WHERE localAddress.StudentUID = student.StudentUID
                  AND localAddress.ActiveFlag = @ActiveFlag
                  AND localAddress.AddressType = @AddressType
            ) address
            WHERE student.StudentUID = @StudentUid
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddAddressParameters(command);
        command.Parameters.Add("@StudentUid", SqlDbType.Int).Value = studentUid;

        var matches = await ReadStudentsAsync(command, cancellationToken);
        return matches.SingleOrDefault();
    }

    public async Task<IReadOnlyList<StudentInformationSystemStudent>> SearchAsync(
        string query,
        int maximumResults = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        maximumResults = Math.Clamp(maximumResults, 1, 50);

        var normalizedQuery = query.Trim();
        var isEmail = normalizedQuery.Contains('@', StringComparison.Ordinal);
        var isNumeric = normalizedQuery.All(char.IsDigit);
        var sql = isEmail ? EmailSearchSql : isNumeric ? NumericSearchSql : NameSearchSql;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddAddressParameters(command);
        command.Parameters.Add("@MaximumResults", SqlDbType.Int).Value = maximumResults;

        if (isEmail)
        {
            command.Parameters.Add("@Email", SqlDbType.VarChar, 320).Value = normalizedQuery;
        }
        else if (isNumeric)
        {
            command.Parameters.Add("@StudentId", SqlDbType.VarChar, 50).Value = normalizedQuery;
            command.Parameters.Add("@StudentUid", SqlDbType.Int).Value =
                int.TryParse(normalizedQuery, out var studentUid) ? studentUid : DBNull.Value;
        }
        else
        {
            command.Parameters.Add("@NameQuery", SqlDbType.VarChar, 322).Value = $"%{normalizedQuery}%";
        }

        return await ReadStudentsAsync(command, cancellationToken);
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

    private static void AddAddressParameters(SqlCommand command)
    {
        command.Parameters.Add("@ActiveFlag", SqlDbType.VarChar, 3).Value = "Yes";
        command.Parameters.Add("@AddressType", SqlDbType.VarChar, 20).Value = "Local";
    }

    private static async Task<List<StudentInformationSystemStudent>> ReadStudentsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<StudentInformationSystemStudent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StudentInformationSystemStudent(
                StudentInformationSystemProvider.ThesisElements,
                Convert.ToString(reader.GetValue(0))!,
                reader.GetString(1).Trim(),
                reader.GetString(2).Trim(),
                reader.GetString(3).Trim(),
                reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                reader.GetString(5).Trim()));
        }

        return results;
    }

    private const string NumericSearchSql = """
        SELECT TOP (@MaximumResults)
            student.StudentUID, student.StudentID, COALESCE(address.Email1, ''),
            student.FirstName, student.PreferredName, student.LastName
        FROM dbo.CAMS_Student_View student
        OUTER APPLY (
            SELECT TOP (1) localAddress.Email1
            FROM dbo.CAMS_StudentAddressList_View localAddress
            WHERE localAddress.StudentUID = student.StudentUID
              AND localAddress.ActiveFlag = @ActiveFlag
              AND localAddress.AddressType = @AddressType
        ) address
        WHERE student.StudentID = @StudentId
           OR (@StudentUid IS NOT NULL AND student.StudentUID = @StudentUid)
        ORDER BY student.LastName, student.FirstName, student.StudentID
        """;

    private const string EmailSearchSql = """
        SELECT TOP (@MaximumResults)
            student.StudentUID, student.StudentID, address.Email1,
            student.FirstName, student.PreferredName, student.LastName
        FROM dbo.CAMS_StudentAddressList_View address
        INNER JOIN dbo.CAMS_Student_View student ON student.StudentUID = address.StudentUID
        WHERE address.ActiveFlag = @ActiveFlag
          AND address.AddressType = @AddressType
          AND address.Email1 = @Email
        ORDER BY student.LastName, student.FirstName, student.StudentID
        """;

    private const string NameSearchSql = """
        SELECT TOP (@MaximumResults)
            student.StudentUID, student.StudentID, COALESCE(address.Email1, ''),
            student.FirstName, student.PreferredName, student.LastName
        FROM dbo.CAMS_Student_View student
        OUTER APPLY (
            SELECT TOP (1) localAddress.Email1
            FROM dbo.CAMS_StudentAddressList_View localAddress
            WHERE localAddress.StudentUID = student.StudentUID
              AND localAddress.ActiveFlag = @ActiveFlag
              AND localAddress.AddressType = @AddressType
        ) address
        WHERE student.FirstName LIKE @NameQuery
           OR student.PreferredName LIKE @NameQuery
           OR student.LastName LIKE @NameQuery
           OR student.FirstName + ' ' + student.LastName LIKE @NameQuery
           OR student.PreferredName + ' ' + student.LastName LIKE @NameQuery
           OR student.LastName + ', ' + student.FirstName LIKE @NameQuery
        ORDER BY student.LastName, student.FirstName, student.StudentID
        """;
}
