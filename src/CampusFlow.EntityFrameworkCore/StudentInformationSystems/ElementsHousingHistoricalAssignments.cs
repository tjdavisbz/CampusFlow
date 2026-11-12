using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed partial class ElementsHousingAssignmentWriter
{
    public async Task<List<HousingHistoricalAssignment>> GetHistoricalAssignmentsAsync(int periodId,
        IReadOnlyCollection<int> studentUids, CancellationToken cancellationToken = default)
    {
        EnsureWriteEnabled();
        if (periodId <= 0 || studentUids.Any(x => x <= 0)) throw new ArgumentException("A valid period and reviewed students are required.");
        if (studentUids.Count == 0) return [];
        var rows = await _historicalAssignments(periodId, studentUids, cancellationToken);
        if (rows.Any(x => x.PeriodId != periodId || !studentUids.Contains(x.StudentUid)) ||
            rows.Select(x => x.AssignmentId).Distinct().Count() != rows.Count ||
            rows.GroupBy(x => (x.StudentUid, x.PeriodRoomId)).Any(x => x.Count() != 1))
            throw new InvalidOperationException("Historical assignment identities are ambiguous. No historical deallocation was sent.");
        foreach (var row in rows) row.EnsureRecoverable();
        return rows;
    }

    public async Task ClearHistoricalAssignmentAsync(HousingHistoricalAssignment reviewed,
        CancellationToken cancellationToken = default)
    {
        reviewed.EnsureRecoverable();
        var current = await GetHistoricalAssignmentsAsync(reviewed.PeriodId, [reviewed.StudentUid], cancellationToken);
        if (!current.Contains(reviewed))
            throw new InvalidOperationException("The reviewed historical assignment changed. Review recovery again.");
        if (await GetAssignedRoomAsync(reviewed.StudentUid, reviewed.PeriodId, cancellationToken) is not null)
            throw new InvalidOperationException("A live room assignment now exists. Historical recovery cannot deallocate an active assignment.");
        // Reinitialization marks the old row deleted but leaves a vendor-side allocation
        // conflict. Explicitly deallocating that exact old room clears the conflict.
        await DeallocateAsync(reviewed.StudentUid, reviewed.PeriodId, reviewed.PeriodRoomId, cancellationToken);
        if (await GetAssignedRoomAsync(reviewed.StudentUid, reviewed.PeriodId, cancellationToken) is not null)
            throw new InvalidOperationException("A live assignment changed during historical recovery. Check Elements before retrying.");
    }

    private async Task<List<HousingHistoricalAssignment>> ReadHistoricalAssignmentsAsync(int periodId,
        IReadOnlyCollection<int> students, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(cancellationToken);
        var rows = new List<HousingHistoricalAssignment>();
        foreach (var chunk in students.Distinct().Chunk(1000))
        {
            var parameters = chunk.Select((_, i) => "@Student" + i).ToArray();
            var sql = """
                SELECT a.HousingPeriodRoomAssignmentID,a.StudentUID,a.HousingPeriodID,r.HousingPeriodRoomID,
                       p.HousingPeriodPropertyID,r.OriginalRoomID,p.PropertyName,r.RoomName,
                       a.IsDeleted,r.IsDeleted,p.IsDeleted,r.RoomRate,r.PerPersonRate
                FROM StudentLife.HousingPeriodRoomAssignments a
                JOIN StudentLife.HousingPeriodRooms r ON r.HousingPeriodRoomID=a.HousingPeriodRoomID AND r.HousingPeriodID=a.HousingPeriodID
                JOIN StudentLife.HousingPeriodProperties p ON p.HousingPeriodPropertyID=r.HousingPeriodPropertyID AND p.HousingPeriodID=a.HousingPeriodID
                WHERE a.HousingPeriodID=@Period AND (r.IsDeleted=1 OR p.IsDeleted=1)
                """ + " AND a.StudentUID IN (" + string.Join(",", parameters) + ") ORDER BY a.StudentUID,a.HousingPeriodRoomAssignmentID";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            command.Parameters.Add("@Period", SqlDbType.Int).Value = periodId;
            for (var i = 0; i < chunk.Length; i++) command.Parameters.Add(parameters[i], SqlDbType.Int).Value = chunk[i];
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.GetInt32(4), reader.IsDBNull(5) ? 0 : reader.GetInt32(5), reader.GetString(6).Trim(), reader.GetString(7).Trim(),
                    reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10),
                    reader.IsDBNull(11) ? null : reader.GetDecimal(11), reader.IsDBNull(12) ? null : reader.GetDecimal(12)));
        }
        return rows;
    }
}
