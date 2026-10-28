using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.Chapel;

public sealed record ChapelAttendanceRow(Guid Id, string Revision, int TermCalendarId,
    int StudentUid, string StudentId, string StudentName, DateTime ChapelAtLocal,
    ChapelAttendanceStatus Status, string? Comment, ChapelAttendanceSource Source)
{
    public bool IsScanInOnly => ChapelScanOutWaiverPolicy.IsEligible(Status, Source, Comment);
}

public sealed record ChapelAttendanceUpdate(Guid Id, string Revision,
    ChapelAttendanceStatus Status, string? Comment);

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
public class ChapelAttendanceAppService : CampusFlowAppService
{
    public const int GeneralSearchLimit = 500;
    public const int DaySearchLimit = 5000;
    private readonly IRepository<ChapelAttendanceRecord, Guid> _records;
    private readonly IRepository<ChapelGroupMembership, Guid> _groups;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public ChapelAttendanceAppService(IRepository<ChapelAttendanceRecord, Guid> records,
        IRepository<ChapelGroupMembership, Guid> groups, IUnitOfWorkManager unitOfWorkManager)
    {
        _records = records;
        _groups = groups;
        _unitOfWorkManager = unitOfWorkManager;
    }

    private Guid Tenant() => CurrentTenant.Id ?? throw new UserFriendlyException("Select a university first.");

    public async Task<List<string>> GetGroupNamesAsync(int termCalendarId)
    {
        var tenant = Tenant();
        if (termCalendarId <= 0) throw new UserFriendlyException("Choose a term.");
        var query = (await _groups.GetQueryableAsync()).Where(x => x.TenantId == tenant && x.TermCalendarId == termCalendarId)
            .Select(x => x.GroupName).Distinct().OrderBy(x => x);
        return await AsyncExecuter.ToListAsync(query);
    }

    public async Task<List<ChapelAttendanceRow>> SearchAsync(int termCalendarId, DateTime? date,
        string? student, string? groupName)
    {
        var tenant = Tenant();
        if (termCalendarId <= 0) throw new UserFriendlyException("Choose a term.");
        if (date is null && string.IsNullOrWhiteSpace(student) && string.IsNullOrWhiteSpace(groupName))
            throw new UserFriendlyException("Enter a chapel date, student, or group to limit the results.");

        var query = (await _records.GetQueryableAsync())
            .Where(x => x.TenantId == tenant && x.TermCalendarId == termCalendarId);
        if (date is not null)
        {
            var start = date.Value.Date;
            var end = start.AddDays(1);
            query = query.Where(x => x.ChapelAtLocal >= start && x.ChapelAtLocal < end);
        }
        if (!string.IsNullOrWhiteSpace(student))
        {
            foreach (var token in SplitStudentSearch(student))
            {
                var fragment = token.ToLowerInvariant();
                query = query.Where(x => x.StudentId.Contains(token) || x.StudentName.ToLower().Contains(fragment));
            }
        }
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var group = groupName.Trim();
            var members = (await _groups.GetQueryableAsync())
                .Where(x => x.TenantId == tenant && x.TermCalendarId == termCalendarId && x.GroupName == group)
                .Select(x => x.StudentUid);
            query = query.Where(x => members.Contains(x.StudentUid));
        }
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.ChapelAtLocal)
            .ThenBy(x => x.StudentName).Take(date.HasValue ? DaySearchLimit + 1 : GeneralSearchLimit + 1));
        return rows.Select(Map).ToList();
    }

    public static string[] SplitStudentSearch(string text)
    {
        if (text.Length > 100) throw new UserFriendlyException("Keep the student search under 100 characters.");
        var tokens = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 8) throw new UserFriendlyException("Use at most eight name or ID fragments.");
        return tokens;
    }

    public async Task<int> CreditScanInOnlySessionAsync(int termCalendarId, DateTime chapelAtLocal, int expectedCount)
    {
        var tenant = Tenant();
        if (termCalendarId <= 0 || chapelAtLocal == default || chapelAtLocal.Kind != DateTimeKind.Unspecified ||
            expectedCount is <= 0 or > DaySearchLimit)
            throw new UserFriendlyException("Choose a chapel session with scan-in-only absences.");

        var central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        if (chapelAtLocal >= TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, central).DateTime)
            throw new UserFriendlyException("A future chapel session cannot be credited.");

        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var query = (await _records.GetQueryableAsync()).Where(x => x.TenantId == tenant &&
            x.TermCalendarId == termCalendarId && x.ChapelAtLocal == chapelAtLocal);
        var sessionRows = await AsyncExecuter.ToListAsync(query.Take(DaySearchLimit + 1));
        if (sessionRows.Count > DaySearchLimit)
            throw new UserFriendlyException("This chapel session has too many records for a bulk correction.");
        var eligible = sessionRows.Where(x => ChapelScanOutWaiverPolicy.IsEligible(x.Status, x.Source, x.Comment)).ToList();
        if (eligible.Count != expectedCount)
            throw new UserFriendlyException("The scan-in-only count changed. Refresh the date before applying a correction.");

        var reviewedAtUtc = DateTime.UtcNow;
        foreach (var record in eligible)
        {
            record.CreditScanInOnlyWithoutScanOut(reviewedAtUtc);
            await _records.UpdateAsync(record, autoSave: false);
        }
        await unitOfWork.CompleteAsync();
        return eligible.Count;
    }

    public async Task<ChapelAttendanceRow> UpdateAsync(ChapelAttendanceUpdate input)
    {
        var tenant = Tenant();
        var record = await _records.GetAsync(input.Id);
        if (record.TenantId != tenant) throw new UserFriendlyException("Attendance record not found.");
        if (record.ConcurrencyStamp != input.Revision)
            throw new UserFriendlyException("This attendance record changed. Reload it before saving.");
        record.SetByStaff(input.Status, input.Comment);
        await _records.UpdateAsync(record, autoSave: true);
        return Map(record);
    }

    private static ChapelAttendanceRow Map(ChapelAttendanceRecord x) =>
        new(x.Id, x.ConcurrencyStamp, x.TermCalendarId, x.StudentUid, x.StudentId,
            x.StudentName, x.ChapelAtLocal, x.Status, x.Comment, x.Source);
}
