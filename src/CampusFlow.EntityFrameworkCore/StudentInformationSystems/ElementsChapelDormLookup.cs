using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Chapel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
namespace CampusFlow.StudentInformationSystems;

public sealed class ElementsChapelDormLookup(IConfiguration configuration) : IChapelDormLookup
{
    public async Task<IReadOnlyList<ChapelResidence>> GetResidencesAsync(int termCalendarId, CancellationToken cancellationToken = default)
    {
        if (termCalendarId <= 0) throw new ArgumentOutOfRangeException(nameof(termCalendarId));
        var cs = configuration.GetConnectionString("ThesisElementsReadOnly") ?? throw new InvalidOperationException("Housing reporting connection is missing.");
        var np = string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) || string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Staging", StringComparison.OrdinalIgnoreCase);
        var parsed = new SqlConnectionStringBuilder(cs); var expected = np ? "sql-elm-nonprod-usc-replica.database.windows.net" : "sql-elm-prod-usc-replica.database.windows.net";
        if (!(string.Equals(parsed.DataSource, expected, StringComparison.OrdinalIgnoreCase) || string.Equals(parsed.DataSource, $"tcp:{expected},1433", StringComparison.OrdinalIgnoreCase)) || parsed.InitialCatalog != (np ? "EL-NP-NEL-DB" : "EL-NEL-DB"))
            throw new InvalidOperationException("The chapel housing lookup must use the matching Elements environment.");
        await using var c = new SqlConnection(cs); await c.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand("""
            WITH Assignments AS (
                SELECT a.StudentUID,p.PropertyName
                FROM StudentLife.HousingPeriodRoomAssignments a
                JOIN StudentLife.HousingPeriods hp ON hp.HousingPeriodID=a.HousingPeriodID AND hp.IsDeleted=0
                JOIN dbo.TermCalendar t ON t.TermCalendarID=@Term AND hp.StartDate<=t.TermEndDate AND hp.EndDate>=t.TermStartDate
                JOIN StudentLife.HousingPeriodRooms r ON r.HousingPeriodRoomID=a.HousingPeriodRoomID AND r.HousingPeriodID=a.HousingPeriodID AND r.IsDeleted=0
                JOIN StudentLife.HousingPeriodProperties p ON p.HousingPeriodPropertyID=r.HousingPeriodPropertyID AND p.HousingPeriodID=a.HousingPeriodID AND p.IsDeleted=0
                WHERE a.IsDeleted=0
            )
            SELECT s.StudentID,s.FirstName,s.LastName,a.PropertyName,ss.Commuter
            FROM dbo.Student s
            LEFT JOIN dbo.CAMS_StudentStatus_View ss ON ss.StudentUID=s.StudentUID AND ss.TermCalendarID=@Term
            LEFT JOIN Assignments a ON a.StudentUID=s.StudentUID
            WHERE ss.StudentStatusID IS NOT NULL OR a.StudentUID IS NOT NULL;
            """, c) { CommandTimeout = 60 };
        cmd.Parameters.Add("@Term", SqlDbType.Int).Value = termCalendarId;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Id, string Name, string? Building, string? Status)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= 50000) throw new InvalidOperationException("Housing data exceeds the reviewed term lookup limit.");
            if (reader.IsDBNull(0) || string.IsNullOrWhiteSpace(reader.GetString(0))) continue;
            rows.Add((reader.GetString(0).Trim(), ((reader.IsDBNull(1) ? "" : reader.GetString(1)) + " " + (reader.IsDBNull(2) ? "" : reader.GetString(2))).Trim(), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return rows.GroupBy(x => x.Id, StringComparer.Ordinal).Select(g => new ChapelResidence(g.Key, g.First().Name, ChapelDorms.Classify(g.Select(x => x.Building), g.Select(x => x.Status)))).ToArray();
    }
}
