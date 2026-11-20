using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed partial class ElementsHousingAssignmentWriter
{
    private async Task ClearMigratedTombstoneAsync(ResidentialHousingImport source,
        ImportedResidentialAssignment migrated, int originalRoomId, CancellationToken ct)
    {
        var rows = await _historicalAssignments(source.PeriodId, [migrated.StudentUid], ct);
        if (rows.Count == 0) return;
        // A reinitialized residential row can still reserve the student in Elements.
        // This is separate from commuter recovery: bind it to the authoritative CAMS
        // resident and the exact original room in the current, zero-rent catalog.
        var old = rows.SingleOrDefault();
        if (source.TermCalendarId != 269 || old is null || old.AssignmentId <= 0 ||
            old.StudentUid != migrated.StudentUid || old.PeriodId != source.PeriodId ||
            old.PeriodRoomId <= 0 || old.PeriodRoomId == migrated.PeriodRoomId || old.PropertyId <= 0 ||
            !old.AssignmentDeleted || !old.RoomDeleted || !old.PropertyDeleted || originalRoomId <= 0 ||
            old.OriginalRoomId != originalRoomId ||
            !string.Equals(old.Building.Trim(), migrated.Building.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(old.Room.Trim(), migrated.Room.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The migrated assignment history is ambiguous. Review it in Elements before applying.");

        var billing = await _migrationBillingSnapshot(migrated.StudentUid, ct);
        var refreshed = await _migratedHousing.ReadForPeriodAsync(source.PeriodId, ct);
        refreshed?.Validate();
        var checkedRows = await _historicalAssignments(source.PeriodId, [migrated.StudentUid], ct);
        if (refreshed?.Assignments.SingleOrDefault(x => x.StudentUid == migrated.StudentUid) != migrated ||
            checkedRows.Count != 1 || checkedRows[0] != old ||
            await GetAssignedRoomAsync(migrated.StudentUid, source.PeriodId, ct) is not null ||
            await _currentAssignmentExists(migrated.StudentUid, source.PeriodId, ct))
            throw new InvalidOperationException("The migrated assignment changed during verification. No historical deallocation was sent.");

        await ChangeAsync("deallocate", old.StudentUid, old.PeriodId, old.PeriodRoomId, ct);
        // Never repeat a write after a timeout or a delayed replica response.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var remaining = await _historicalAssignments(source.PeriodId, [migrated.StudentUid], ct);
            if (remaining.Count == 0)
            {
                if (await _migrationBillingSnapshot(migrated.StudentUid, ct) != billing ||
                    await GetAssignedRoomAsync(migrated.StudentUid, source.PeriodId, ct) is not null ||
                    await _currentAssignmentExists(migrated.StudentUid, source.PeriodId, ct))
                    throw new InvalidOperationException("Housing recovery could not confirm unchanged billing and room assignments. Review the saved operation before retrying.");
                return;
            }
            if (remaining.Count != 1 || remaining[0] != old)
                throw new InvalidOperationException("Historical housing changed during recovery. Review the saved operation before retrying.");
            await Task.Delay(1000, ct);
        }
        throw new InvalidOperationException("Historical deallocation is still updating in Elements. Review the saved operation before retrying.");
    }

    private async Task<string> ReadMigrationBillingSnapshotAsync(int studentUid, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_configuration.GetConnectionString("ThesisElementsReadOnly"));
        await connection.OpenAsync(ct);
        // Old room prices can be nonzero. They do not authorize changes to any money.
        // Compare every nonzero ledger/batch row across all terms, including reversals.
        await using var command = new SqlCommand("""
            SELECT * FROM (
                SELECT 'Ledger' Location, BillingID Id, TermCalendarID, TransDocID, Debits, Credits,
                    ShowAmount, RefNo, Voided, Reversing
                FROM dbo.Billing WHERE OwnerUID=@Student AND (Debits<>0 OR Credits<>0 OR ShowAmount<>0)
                UNION ALL
                SELECT 'Batch', BillingBatchID, TermCalendarID, TransDocID, Debits, Credits,
                    ShowAmount, RefNo, NULL, NULL
                FROM dbo.BillingBatch WHERE OwnerUID=@Student AND (Debits<>0 OR Credits<>0 OR ShowAmount<>0)
            ) rows ORDER BY Location,Id FOR JSON PATH, INCLUDE_NULL_VALUES
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@Student", SqlDbType.Int).Value = studentUid;
        var json = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) json.Append(reader.GetString(0));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json.ToString())));
    }
}
