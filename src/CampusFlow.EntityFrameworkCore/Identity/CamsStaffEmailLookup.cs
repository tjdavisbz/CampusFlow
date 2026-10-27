using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.EntityFrameworkCore.Identity;

public sealed class CamsStaffEmailLookup(IConfiguration configuration, IMemoryCache cache) :
    ICamsStaffEmailLookup, ITransientDependency
{
    private const string CacheKey = "CampusFlow:CamsStaffEmails";

    public async Task<IReadOnlyCollection<string>> GetNormalizedEmailsAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out string[]? cached) && cached is not null)
            return cached;

        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Elements read-only connection is not configured.");

        var emails = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT DISTINCT EmailAddress FROM dbo.CAMSUser WHERE NULLIF(LTRIM(RTRIM(EmailAddress)), '') IS NOT NULL",
            connection) { CommandType = CommandType.Text };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var normalized = reader.GetString(0).Trim().ToUpperInvariant();
            if (normalized.Length > 0) emails.Add(normalized);
        }

        var result = new string[emails.Count];
        emails.CopyTo(result);
        cache.Set(CacheKey, result, TimeSpan.FromMinutes(2));
        return result;
    }
}
