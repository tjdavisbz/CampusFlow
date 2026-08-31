using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsAdvisorLookup : IStudentInformationSystemAdvisorLookup
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private readonly IConfiguration _configuration;

    public ThesisElementsAdvisorLookup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<AdvisorLookupResult?> FindAsync(
        string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = email.Trim();
        var localPart = normalizedEmail.Split('@', 2)[0];
        const string sql = """
            SELECT TOP (2)
                CAMSUserID,
                LTRIM(RTRIM(CAMSUser)),
                LTRIM(RTRIM(EmailAddress)),
                LTRIM(RTRIM(FirstName)),
                LTRIM(RTRIM(LastName)),
                LTRIM(RTRIM(CONCAT(FirstName, ' ', LastName)))
            FROM dbo.CAMSUser
            WHERE DisableLogin = 0
              AND (
                    LOWER(LTRIM(RTRIM(EmailAddress))) = LOWER(@Email)
                 OR (
                        LOWER(LTRIM(RTRIM(CAMSUser))) = LOWER(@UserName)
                    AND NOT EXISTS
                        (
                            SELECT 1
                            FROM dbo.CAMSUser exactUser
                            WHERE exactUser.DisableLogin = 0
                              AND LOWER(LTRIM(RTRIM(exactUser.EmailAddress))) = LOWER(@Email)
                        )
                    )
              )
            ORDER BY CASE WHEN LOWER(LTRIM(RTRIM(EmailAddress))) = LOWER(@Email) THEN 0 ELSE 1 END,
                     CAMSUserID
            """;

        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Email", SqlDbType.VarChar, 256).Value = normalizedEmail;
        command.Parameters.Add("@UserName", SqlDbType.VarChar, 64).Value = localPart;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var id = reader.GetInt32(0).ToString();
        var userName = reader.GetString(1);
        var elementsEmail = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var firstName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        var lastName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var displayName = reader.IsDBNull(5) ? userName : reader.GetString(5);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The Microsoft account matched more than one active Elements user.");

        var globalReviewers = _configuration
            .GetSection("AdvisorPortal:LegacyGlobalReviewerUsernames")
            .Get<string[]>() ?? [];
        return new AdvisorLookupResult(id, userName,
            string.IsNullOrWhiteSpace(elementsEmail) ? normalizedEmail : elementsEmail,
            firstName, lastName, displayName,
            globalReviewers.Contains(userName, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<AdvisorLookupResult>> SearchAsync(
        string search, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2) return [];
        const string sql = """
            SELECT TOP (12)
                CAMSUserID, LTRIM(RTRIM(CAMSUser)), LTRIM(RTRIM(EmailAddress)),
                LTRIM(RTRIM(FirstName)), LTRIM(RTRIM(LastName)),
                LTRIM(RTRIM(CONCAT(FirstName, ' ', LastName)))
            FROM dbo.CAMSUser
            WHERE DisableLogin = 0
              AND (LOWER(LTRIM(RTRIM(CAMSUser))) LIKE @Search
                OR LOWER(LTRIM(RTRIM(EmailAddress))) LIKE @Search
                OR LOWER(LTRIM(RTRIM(CONCAT(FirstName, ' ', LastName)))) LIKE @Search)
            ORDER BY LastName, FirstName, CAMSUserID
            """;
        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        var results = new List<AdvisorLookupResult>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Search", SqlDbType.VarChar, 260).Value = $"%{search.Trim().ToLowerInvariant()}%";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userName = reader.GetString(1);
            var email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            if (string.IsNullOrWhiteSpace(email)) continue;
            results.Add(new AdvisorLookupResult(reader.GetInt32(0).ToString(), userName, email,
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? userName : reader.GetString(5), false));
        }
        return results;
    }
}
