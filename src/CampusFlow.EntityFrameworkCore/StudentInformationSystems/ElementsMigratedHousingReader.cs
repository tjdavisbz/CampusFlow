using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

// These migrated assignments remain in Elements' CAMS housing tables after the
// StudentLife period was reinitialized. No CampusFlow draft is used as a student source.
public sealed class ElementsMigratedHousingReader(IConfiguration configuration)
    : IElementsMigratedHousingReader, ITransientDependency
{
    public Task<ResidentialHousingImport?> ReadForPeriodAsync(int periodId, CancellationToken cancellationToken = default) =>
        ReadAsync(periodId, null, cancellationToken);

    public Task<ResidentialHousingImport?> ReadForTermAsync(int termCalendarId, CancellationToken cancellationToken = default) =>
        termCalendarId == 269 ? ReadAsync(null, termCalendarId, cancellationToken) : Task.FromResult<ResidentialHousingImport?>(null);

    private async Task<ResidentialHousingImport?> ReadAsync(int? periodId, int? termCalendarId, CancellationToken cancellationToken)
    {
        var settings = new SqlConnectionStringBuilder(configuration.GetConnectionString("ThesisElementsReadOnly"))
            { ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // Limit this repair to the reviewed migrated Spring 2027 source. Other periods
        // continue to use their existing StudentLife records.
        await using var period = new SqlCommand("""
            SELECT hp.HousingPeriodID, hp.HousingPeriodName, t.TermCalendarID
            FROM StudentLife.HousingPeriods hp
            JOIN dbo.TermCalendar t ON CAST(hp.StartDate AS date)=CAST(t.TermStartDate AS date)
                AND CAST(hp.EndDate AS date)=CAST(t.TermEndDate AS date)
            WHERE hp.IsDeleted=0 AND t.TermCalendarID=269
                AND (@PeriodId IS NULL OR hp.HousingPeriodID=@PeriodId)
                AND (@TermId IS NULL OR t.TermCalendarID=@TermId);
            """, connection) { CommandTimeout = 60 };
        period.Parameters.Add("@PeriodId", SqlDbType.Int).Value = (object?)periodId ?? DBNull.Value;
        period.Parameters.Add("@TermId", SqlDbType.Int).Value = (object?)termCalendarId ?? DBNull.Value;
        ResidentialHousingImport result;
        await using (var reader = await period.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            result = new() { PeriodId = reader.GetInt32(0), PeriodName = reader.GetString(1),
                TermCalendarId = reader.GetInt32(2) };
            if (await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The migrated housing period is ambiguous.");
        }
        await using var assignments = new SqlCommand("""
            SELECT v.OwnerID, room.HousingPeriodRoomID, v.AssignmentID, v.RoomID,
                property.PropertyName, room.RoomName,
                LTRIM(RTRIM(v.FirstName))+' '+LTRIM(RTRIM(v.LastName))
            FROM dbo.CAMS_Housing_Assignment_View v
            JOIN dbo.CAMS_Housing_Rooms_View legacyRoom ON legacyRoom.RoomID=v.RoomID
            LEFT JOIN dbo.TPS_HousingCrosswalk crosswalk ON crosswalk.RoomID=v.RoomID
            LEFT JOIN StudentLife.HousingPeriodRooms room ON room.OriginalRoomID=crosswalk.SLRoomID
                AND room.HousingPeriodID=@PeriodId AND room.IsDeleted=0
            LEFT JOIN StudentLife.HousingPeriodProperties property
                ON property.HousingPeriodPropertyID=room.HousingPeriodPropertyID
                AND property.HousingPeriodID=@PeriodId AND property.IsDeleted=0
                AND property.OriginalPropertyID=crosswalk.PropertyID
            WHERE v.TermCalendarID=@TermId AND v.Type='Student'
                AND LTRIM(RTRIM(legacyRoom.Building)) NOT IN ('Commuter','Commuter Dorm')
                -- Deallocation hard-deletes the current row. Elements' temporal
                -- history retains its authority over stale CAMS data. Only the
                -- current room catalog counts; reinitialized old rooms do not.
                AND NOT EXISTS (
                    SELECT 1 FROM StudentLife.HousingPeriodRoomAssignments FOR SYSTEM_TIME ALL AS currentAssignment
                    JOIN StudentLife.HousingPeriodRooms currentRoom
                      ON currentRoom.HousingPeriodRoomID=currentAssignment.HousingPeriodRoomID
                     AND currentRoom.HousingPeriodID=@PeriodId AND currentRoom.IsDeleted=0
                    JOIN StudentLife.HousingPeriodProperties currentProperty
                      ON currentProperty.HousingPeriodPropertyID=currentRoom.HousingPeriodPropertyID
                     AND currentProperty.HousingPeriodID=@PeriodId AND currentProperty.IsDeleted=0
                    WHERE currentAssignment.StudentUID=v.OwnerID AND currentAssignment.HousingPeriodID=@PeriodId)
            ORDER BY v.OwnerID, v.AssignmentID;
            """, connection) { CommandTimeout = 60 };
        assignments.Parameters.Add("@PeriodId", SqlDbType.Int).Value = result.PeriodId;
        assignments.Parameters.Add("@TermId", SqlDbType.Int).Value = result.TermCalendarId;
        await using (var reader = await assignments.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(1) || reader.IsDBNull(4) || reader.IsDBNull(5) || reader.IsDBNull(6))
                    throw new InvalidOperationException("A migrated residential assignment has no unique current Elements room mapping.");
                result.Assignments.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6).Trim()));
            }
        result.Validate();
        result.SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result.Assignments)))).ToLowerInvariant();
        return result;
    }
}
