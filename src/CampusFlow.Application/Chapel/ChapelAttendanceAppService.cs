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
    private readonly IChapelDormLookup _dorms;

    public ChapelAttendanceAppService(IRepository<ChapelAttendanceRecord, Guid> records,
        IRepository<ChapelGroupMembership, Guid> groups, IUnitOfWorkManager unitOfWorkManager, IChapelDormLookup dorms)
    {
        _records = records;
        _groups = groups;
        _unitOfWorkManager = unitOfWorkManager; _dorms = dorms;
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
        string? student, string? groupName, string? dorm = null)
    {
        var tenant = Tenant();
        if (termCalendarId <= 0) throw new UserFriendlyException("Choose a term.");
        if (date is null && string.IsNullOrWhiteSpace(student) && string.IsNullOrWhiteSpace(groupName) && string.IsNullOrWhiteSpace(dorm))
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
        if (!string.IsNullOrWhiteSpace(dorm))
        {
            EnsureNelson(); if (!ChapelDorms.IsValid(dorm)) throw new UserFriendlyException("Choose an available dorm.");
            var residences = await _dorms.GetResidencesAsync(termCalendarId);
            var ids = residences.Where(x => x.DormKey == dorm).Select(x => x.StudentId).ToArray();
            var known = residences.Select(x => x.StudentId).ToArray();
            query = dorm == "unknown" ? query.Where(x => ids.Contains(x.StudentId) || !known.Contains(x.StudentId)) : query.Where(x => ids.Contains(x.StudentId));
        }
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.ChapelAtLocal)
            .ThenBy(x => x.StudentName).Take(date.HasValue ? DaySearchLimit + 1 : GeneralSearchLimit + 1));
        return rows.Select(Map).ToList();
    }

    public sealed record DormRosterRow(string StudentId, string StudentName, string DormKey, bool HasChapelRecords);
    public sealed record DormRosterPage(IReadOnlyList<DormRosterRow> Students, int Total, int Page, int PageSize);
    public async Task<DormRosterPage> GetDormRosterAsync(int termCalendarId, string dorm, int page = 1, string? student = null)
    {
        Tenant(); EnsureNelson(); if (termCalendarId <= 0 || !ChapelDorms.IsValid(dorm) || page < 1 || page > 1000) throw new UserFriendlyException("Choose a term, dorm and valid page.");
        var residences = (await _dorms.GetResidencesAsync(termCalendarId)).ToList();
        var chapelQuery = (await _records.GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.TermCalendarId == termCalendarId);
        if (dorm == "unknown")
        {
            var chapel = await AsyncExecuter.ToListAsync(chapelQuery.Select(x => new { x.StudentId, x.StudentName }).Distinct());
            var known = residences.Select(x => x.StudentId).ToHashSet(StringComparer.Ordinal);
            foreach (var row in chapel.GroupBy(x => x.StudentId).Where(x => !known.Contains(x.Key))) residences.Add(new(row.Key, row.First().StudentName, "unknown"));
        }
        IEnumerable<ChapelResidence> rows = residences.Where(x => x.DormKey == dorm);
        if (!string.IsNullOrWhiteSpace(student)) foreach (var token in SplitStudentSearch(student)) rows = rows.Where(x => x.StudentId.Contains(token, StringComparison.OrdinalIgnoreCase) || x.StudentName.Contains(token, StringComparison.OrdinalIgnoreCase));
        var sorted = rows.OrderBy(x => x.StudentName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.StudentId, StringComparer.Ordinal).ToArray();
        var visible = sorted.Skip((page - 1) * 100).Take(100).ToArray(); var visibleIds = visible.Select(x => x.StudentId).ToArray();
        var attended = (await AsyncExecuter.ToListAsync(chapelQuery.Where(x => visibleIds.Contains(x.StudentId)).Select(x => x.StudentId).Distinct())).ToHashSet(StringComparer.Ordinal);
        return new(visible.Select(x => new DormRosterRow(x.StudentId, x.StudentName, x.DormKey, attended.Contains(x.StudentId))).ToArray(), sorted.Length, page, 100);
    }
    public async Task<ChapelAttendanceRow> GetRowAsync(Guid id)
    {
        var tenant = Tenant(); var record = await _records.FindAsync(id) ?? throw new UserFriendlyException("Attendance record not found.");
        if (record.TenantId != tenant) throw new UserFriendlyException("Attendance record not found."); return Map(record);
    }
    private void EnsureNelson()
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) throw new UserFriendlyException("This housing lookup is available only for Nelson.");
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

    [UnitOfWork(IsDisabled = true)]
    public async Task<ChapelAttendanceRow> UpdateAsync(ChapelAttendanceUpdate input)
    {
        var tenant = Tenant();
        using var write = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var record = await _records.FindAsync(input.Id) ?? throw new UserFriendlyException("Attendance record not found.");
        if (record.TenantId != tenant) throw new UserFriendlyException("Attendance record not found.");
        if (record.ConcurrencyStamp != input.Revision)
            throw new UserFriendlyException("This attendance record changed. Reload it before saving.");
        if (!Enum.IsDefined(input.Status) || input.Comment?.Length > 4000) throw new UserFriendlyException("Choose a valid status and a comment of at most 4,000 characters.");
        var normalized = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        var existing = string.IsNullOrWhiteSpace(record.Comment) ? null : record.Comment.Trim();
        if (record.Status == input.Status && existing == normalized) return Map(record);
        record.SetByStaff(input.Status, normalized);
        await _records.UpdateAsync(record, autoSave: true);
        await write.CompleteAsync();
        return Map(record);
    }

    private static ChapelAttendanceRow Map(ChapelAttendanceRecord x) =>
        new(x.Id, x.ConcurrencyStamp, x.TermCalendarId, x.StudentUid, x.StudentId,
            x.StudentName, x.ChapelAtLocal, x.Status, x.Comment, x.Source);
}
