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
                    Name = (reader.GetString(5) + " " + reader.GetString(6)).Trim(),
                    Rate = "Standard"
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

    public async Task<HousingPricingContext> GetPricingAsync(int periodId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT HousingPeriodName, StartDate, EndDate
            FROM StudentLife.HousingPeriods
            WHERE HousingPeriodID = @PeriodId AND IsDeleted = 0;

            SELECT COUNT(*) AS Properties,
                   SUM(CASE WHEN rent.HousingPeriodRentID IS NOT NULL
                                 AND rent.Rent = 'Room Setup' AND rent.TransDocID = 137
                                 AND rent.TransactionCategoryID = 7
                                 AND rent.PaymentFrequencyID = 1 THEN 1 ELSE 0 END) AS ZeroRentConfigured,
                   SUM(CASE WHEN room.HousingPeriodRoomID IS NOT NULL
                                 AND (room.PerPersonRate <> 0 OR room.RoomRate <> 0) THEN 1 ELSE 0 END) AS NonzeroRooms
            FROM StudentLife.HousingPeriodProperties property
            LEFT JOIN StudentLife.HousingPeriodRents rent
              ON rent.HousingPeriodPropertyID = property.HousingPeriodPropertyID
             AND rent.HousingPeriodID = property.HousingPeriodID AND rent.IsDeleted = 0
            LEFT JOIN StudentLife.HousingPeriodRooms room
              ON room.HousingPeriodPropertyID = property.HousingPeriodPropertyID
             AND room.HousingPeriodID = property.HousingPeriodID AND room.IsDeleted = 0
            WHERE property.HousingPeriodID = @PeriodId AND property.IsDeleted = 0;

            SELECT fee.FeeName, fee.TransDocID, CAST(fee.Amount AS decimal(12,2)), doc.ARTypeID
            FROM dbo.Housing_Fees fee
            JOIN dbo.Transdoc doc ON doc.TransDocID = fee.TransDocID AND doc.TransType = 'DEBIT'
            WHERE fee.TermCalendarID = 268;

            SELECT TransDocID, ARTypeID FROM dbo.Transdoc
            WHERE TransDoc = 'Single' AND TransType = 'DEBIT' AND ActiveFlag = 1;
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@PeriodId", SqlDbType.Int).Value = periodId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || periodId != 1 ||
            !string.Equals(reader.GetString(0), "Fall 2026 Prod", StringComparison.OrdinalIgnoreCase) ||
            reader.GetDateTime(1).Date != new DateTime(2026, 8, 24) ||
            reader.GetDateTime(2).Date != new DateTime(2026, 12, 11))
            throw new InvalidOperationException("The housing period has not been approved for CampusFlow billing.");
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != 536 ||
            reader.GetInt32(1) != 536 || reader.GetInt32(2) != 0)
            throw new InvalidOperationException("Elements housing rent must be configured at $0 for every room before CampusFlow calculates charges.");
        await reader.NextResultAsync(cancellationToken);
        var fees = new List<HousingTermFee>();
        while (await reader.ReadAsync(cancellationToken))
            fees.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2), reader.GetInt32(3)));
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The Single housing charge transaction document is missing.");
        var singleDocumentId = reader.GetInt32(0);
        var singleAccountsReceivableTypeId = reader.GetInt32(1);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The Single housing charge transaction document is ambiguous.");
        return new HousingPricingContext(268, fees, singleDocumentId, singleAccountsReceivableTypeId);
    }

    public async Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2 || periodId <= 0) return [];
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (20) s.StudentUID, s.StudentID, s.FirstName, s.MiddleName,
                   s.LastName, s.ProspectStatus, s.Campus,
                   room.PropertyName, room.RoomName
            FROM dbo.CAMS_Student_View s
            OUTER APPLY (
                SELECT TOP (1) p.PropertyName, r.RoomName
                FROM StudentLife.HousingPeriodRoomAssignments a
                JOIN StudentLife.HousingPeriodRooms r
                  ON r.HousingPeriodRoomID = a.HousingPeriodRoomID
                 AND r.HousingPeriodID = a.HousingPeriodID AND r.IsDeleted = 0
                JOIN StudentLife.HousingPeriodProperties p
                  ON p.HousingPeriodPropertyID = r.HousingPeriodPropertyID
                 AND p.HousingPeriodID = r.HousingPeriodID AND p.IsDeleted = 0
                WHERE a.StudentUID = s.StudentUID AND a.HousingPeriodID = @PeriodId AND a.IsDeleted = 0
                ORDER BY a.CreatedOn DESC, a.HousingPeriodRoomAssignmentID DESC
            ) room
            WHERE s.StudentID = @Query OR s.StudentUID = TRY_CONVERT(int, @Query)
               OR s.FirstName LIKE @NameQuery OR s.PreferredName LIKE @NameQuery
               OR s.LastName LIKE @NameQuery
               OR s.FirstName + ' ' + s.LastName LIKE @NameQuery
               OR s.LastName + ', ' + s.FirstName LIKE @NameQuery
            ORDER BY s.LastName, s.FirstName, s.StudentID;
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@PeriodId", SqlDbType.Int).Value = periodId;
        command.Parameters.Add("@Query", SqlDbType.VarChar, 50).Value = query.Trim();
        command.Parameters.Add("@NameQuery", SqlDbType.VarChar, 322).Value = $"%{query.Trim()}%";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<HousingStudentOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string Value(int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal).Trim();
            results.Add(new HousingStudentOption
            {
                StudentUid = reader.GetInt32(0), StudentId = Value(1),
                Name = $"{Value(2)} {Value(4)}".Trim(), MiddleName = Value(3),
                ApplicantStatus = Value(5), Campus = Value(6),
                CurrentRoom = reader.IsDBNull(7) ? "Not assigned" : $"{Value(7)} / {Value(8)}"
            });
        }
        return results;
    }

    public async Task<StudentHousingAssignment?> GetStudentAssignmentAsync(int studentUid,
        CancellationToken cancellationToken = default)
    {
        if (studentUid <= 0) return null;
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (1) a.HousingPeriodID, a.HousingPeriodRoomID,
                   hp.HousingPeriodName, p.PropertyName, r.RoomName
            FROM StudentLife.HousingPeriodRoomAssignments a
            JOIN StudentLife.HousingPeriods hp
              ON hp.HousingPeriodID = a.HousingPeriodID AND hp.IsDeleted = 0
            JOIN StudentLife.HousingPeriodRooms r
              ON r.HousingPeriodRoomID = a.HousingPeriodRoomID
             AND r.HousingPeriodID = a.HousingPeriodID AND r.IsDeleted = 0
            JOIN StudentLife.HousingPeriodProperties p
              ON p.HousingPeriodPropertyID = r.HousingPeriodPropertyID
             AND p.HousingPeriodID = a.HousingPeriodID AND p.IsDeleted = 0
            WHERE a.StudentUID = @StudentUid AND a.IsDeleted = 0
            ORDER BY CASE WHEN SYSUTCDATETIME() BETWEEN hp.StartDate AND hp.EndDate THEN 0
                          WHEN hp.StartDate > SYSUTCDATETIME() THEN 1 ELSE 2 END,
                     hp.StartDate DESC, a.HousingPeriodRoomAssignmentID DESC;
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@StudentUid", SqlDbType.Int).Value = studentUid;
        int periodId, roomId;
        StudentHousingAssignment result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            periodId = reader.GetInt32(0); roomId = reader.GetInt32(1);
            result = new StudentHousingAssignment
            {
                PeriodName = reader.GetString(2), Building = reader.GetString(3), Room = reader.GetString(4)
            };
        }
        await using var roommates = new SqlCommand("""
            SELECT s.FirstName, s.LastName
            FROM StudentLife.HousingPeriodRoomAssignments a
            JOIN dbo.Student s ON s.StudentUID = a.StudentUID
            WHERE a.HousingPeriodID = @PeriodId AND a.HousingPeriodRoomID = @RoomId
              AND a.StudentUID <> @StudentUid AND a.IsDeleted = 0
            ORDER BY s.LastName, s.FirstName;
            """, connection) { CommandTimeout = 60 };
        roommates.Parameters.Add("@PeriodId", SqlDbType.Int).Value = periodId;
        roommates.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId;
        roommates.Parameters.Add("@StudentUid", SqlDbType.Int).Value = studentUid;
        await using var roommateReader = await roommates.ExecuteReaderAsync(cancellationToken);
        while (await roommateReader.ReadAsync(cancellationToken))
            result.Roommates.Add($"{roommateReader.GetString(0)} {roommateReader.GetString(1)}".Trim());
        return result;
    }
}
