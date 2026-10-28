using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Chapel;

public sealed record ChapelAttendanceRow(Guid Id, string Revision, int TermCalendarId,
    int StudentUid, string StudentId, string StudentName, DateTime ChapelAtLocal,
    ChapelAttendanceStatus Status, string? Comment, ChapelAttendanceSource Source);

public sealed record ChapelAttendanceUpdate(Guid Id, string Revision,
    ChapelAttendanceStatus Status, string? Comment);

[Authorize(CampusFlowPermissions.Admin.ChapelAttendance)]
public class ChapelAttendanceAppService : CampusFlowAppService
{
    private readonly IRepository<ChapelAttendanceRecord, Guid> _records;
    private readonly IRepository<ChapelGroupMembership, Guid> _groups;

    public ChapelAttendanceAppService(IRepository<ChapelAttendanceRecord, Guid> records,
        IRepository<ChapelGroupMembership, Guid> groups)
    {
        _records = records;
        _groups = groups;
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
            var text = student.Trim();
            query = query.Where(x => x.StudentId.Contains(text) || x.StudentName.Contains(text));
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
            .ThenBy(x => x.StudentName).Take(500));
        return rows.Select(Map).ToList();
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
