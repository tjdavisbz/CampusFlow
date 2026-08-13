using System;
using System.Collections.Generic;
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

        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        const string sql = """
            SELECT TOP (2) StudentUID, Email1
            FROM [dbo].[CAMS_StudentAddressList_View]
            WHERE ActiveFlag = @ActiveFlag
              AND AddressType = @AddressType
              AND LTRIM(RTRIM(Email1)) = @Email
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ActiveFlag", "Yes");
        command.Parameters.AddWithValue("@AddressType", "Local");
        command.Parameters.AddWithValue("@Email", email.Trim());

        var matches = new List<StudentInformationSystemStudent>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new StudentInformationSystemStudent(
                Convert.ToString(reader.GetValue(0))!,
                reader.GetString(1).Trim()));
        }

        return matches.Count switch
        {
            0 => StudentLookupResult.NotFound(),
            1 => StudentLookupResult.Matched(matches[0]),
            _ => StudentLookupResult.Ambiguous()
        };
    }
}
