using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

// The replica is suitable for planning. A future sync must recheck authoritative API state.
public class ElementsHousingReader(IConfiguration configuration) : IElementsHousingReader, ITransientDependency
{
    private SqlConnection Connection() => new(configuration.GetConnectionString("ThesisElementsReadOnly")
        ?? throw new InvalidOperationException("The Elements read-only connection is not configured."));

    public async Task<List<HousingPeriodOption>> GetPeriodsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT HousingPeriodID, HousingPeriodName
            FROM StudentLife.HousingPeriods
            WHERE IsDeleted = 0
            ORDER BY StartDate DESC, HousingPeriodID DESC;
            """, connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var periods = new List<HousingPeriodOption>();
        while (await reader.ReadAsync(cancellationToken))
            periods.Add(new() { Id = reader.GetInt32(0), Name = reader.GetString(1) });
        return periods;
    }

    public async Task<List<HousingWorkspaceRoom>> GetRoomsAsync(int periodId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT r.HousingPeriodRoomID, p.PropertyName, r.RoomName, r.Occupancy,
                   a.StudentUID, s.FirstName, s.LastName
            FROM StudentLife.HousingPeriodRooms r
            JOIN StudentLife.HousingPeriodProperties p
              ON p.HousingPeriodPropertyID = r.HousingPeriodPropertyID
             AND p.HousingPeriodID = r.HousingPeriodID AND p.IsDeleted = 0
            LEFT JOIN StudentLife.HousingPeriodRoomAssignments a
              ON a.HousingPeriodRoomID = r.HousingPeriodRoomID
             AND a.HousingPeriodID = r.HousingPeriodID AND a.IsDeleted = 0
            LEFT JOIN dbo.Student s ON s.StudentUID = a.StudentUID
            WHERE r.HousingPeriodID = @PeriodId AND r.IsDeleted = 0
            ORDER BY p.PropertyName, r.RoomName, r.HousingPeriodRoomID, a.StudentUID;

            SELECT COUNT(*) FROM StudentLife.HousingPeriodRoomAssignments
            WHERE HousingPeriodID = @PeriodId AND IsDeleted = 0;
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@PeriodId", SqlDbType.Int).Value = periodId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rooms = new Dictionary<int, HousingWorkspaceRoom>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            if (!rooms.TryGetValue(id, out var room))
            {
                if (reader.IsDBNull(3))
                    throw new InvalidOperationException("A room has no configured capacity. Correct it in Elements before refreshing.");
                room = new()
                {
                    Id = id, Building = reader.GetString(1), Name = reader.GetString(2),
                    Capacity = reader.GetInt32(3)
                };
                rooms.Add(id, room);
            }
            if (!reader.IsDBNull(4))
            {
                if (reader.IsDBNull(5) || reader.IsDBNull(6))
                    throw new InvalidOperationException("An assigned student could not be resolved. The draft was not replaced.");
                room.Occupants.Add(new()
                {
                    StudentUid = reader.GetInt32(4),
                    Name = (reader.GetString(5) + " " + reader.GetString(6)).Trim()
                });
            }
        }
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            reader.GetInt32(0) != rooms.Values.Sum(x => x.Occupants.Count))
            throw new InvalidOperationException("Some assignments reference unavailable rooms, or data changed during refresh. The draft was not replaced.");
        if (rooms.Count == 0)
            throw new InvalidOperationException("No rooms are configured for this period. The existing draft was not replaced.");
        return rooms.Values.ToList();
    }
}
