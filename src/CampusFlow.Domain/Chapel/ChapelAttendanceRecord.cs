using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Chapel;

public enum ChapelAttendanceStatus { Pending, Present, Absent, Excused }
public enum ChapelAttendanceSource { LegacyImport, CbordImport, Staff, Scheduled }
public enum ChapelImportDecisionResult { Applied, AlreadyApplied, RequiresReview }

public class ChapelAttendanceRecord : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int TermCalendarId { get; private set; }
    public int StudentUid { get; private set; }
    public string StudentId { get; private set; } = "";
    public string StudentName { get; private set; } = "";
    // CAMS/CBORD chapel times are campus-local. Do not interpret this as UTC.
    public DateTime ChapelAtLocal { get; private set; }
    public ChapelAttendanceStatus Status { get; private set; }
    public string? Comment { get; private set; }
    public ChapelAttendanceSource Source { get; private set; }
    public int? LegacyAttendanceId { get; private set; }

    protected ChapelAttendanceRecord() { }

    public ChapelAttendanceRecord(Guid id, Guid? tenantId, int termCalendarId, int studentUid,
        string studentId, string studentName, DateTime chapelAtLocal,
        ChapelAttendanceStatus status, string? comment, int? legacyAttendanceId = null) : base(id)
    {
        if (termCalendarId <= 0 || studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        TenantId = tenantId;
        TermCalendarId = termCalendarId;
        StudentUid = studentUid;
        StudentId = studentId.Trim();
        StudentName = studentName.Trim();
        ChapelAtLocal = DateTime.SpecifyKind(chapelAtLocal, DateTimeKind.Unspecified);
        Status = status;
        Comment = comment;
        LegacyAttendanceId = legacyAttendanceId;
        Source = ChapelAttendanceSource.LegacyImport;
    }

    public static ChapelAttendanceRecord CreateScheduled(Guid id, Guid? tenantId, int termCalendarId,
        int studentUid, string studentId, string studentName, DateTime chapelAtLocal) =>
        new(id, tenantId, termCalendarId, studentUid, studentId, studentName,
            chapelAtLocal, ChapelAttendanceStatus.Pending, null)
        {
            Source = ChapelAttendanceSource.Scheduled
        };

    public static ChapelAttendanceRecord CreateFromLegacy(Guid id, Guid? tenantId,
        int legacyAttendanceId, int termCalendarId, int studentUid, string studentId,
        string studentName, DateTime chapelAtLocal, bool present, bool absent,
        bool excused, string? comment)
    {
        if (legacyAttendanceId <= 0) throw new ArgumentOutOfRangeException(nameof(legacyAttendanceId));
        // The legacy student portal checks Present first, then Absent, then Excused.
        // Keep that display behavior while preserving all original flags for audit.
        var status = present ? ChapelAttendanceStatus.Present :
            absent ? ChapelAttendanceStatus.Absent :
            excused ? ChapelAttendanceStatus.Excused : ChapelAttendanceStatus.Pending;
        var record = new ChapelAttendanceRecord(id, tenantId, termCalendarId, studentUid,
            studentId, studentName, chapelAtLocal, status, comment, legacyAttendanceId);
        record.ExtraProperties["LegacyPresent"] = present;
        record.ExtraProperties["LegacyAbsent"] = absent;
        record.ExtraProperties["LegacyExcused"] = excused;
        record.ExtraProperties["LegacyFlagConflict"] = (present ? 1 : 0) +
            (absent ? 1 : 0) + (excused ? 1 : 0) > 1;
        return record;
    }

    /// <summary>
    /// Applies a prospective CBORD decision once. Historical rows and staff decisions are
    /// never rewritten by a file retry; conflicting later decisions require staff review.
    /// </summary>
    public ChapelImportDecisionResult ApplyCbordDecision(ChapelAttendanceStatus status, string? comment)
    {
        if (status != ChapelAttendanceStatus.Present && status != ChapelAttendanceStatus.Absent)
            throw new ArgumentOutOfRangeException(nameof(status));
        if (comment?.Length > 4000) throw new ArgumentException("Comment is too long.", nameof(comment));
        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (Source == ChapelAttendanceSource.CbordImport &&
            Status == status && Comment == normalizedComment)
            return ChapelImportDecisionResult.AlreadyApplied;
        if (Source != ChapelAttendanceSource.Scheduled || Status != ChapelAttendanceStatus.Pending)
            return ChapelImportDecisionResult.RequiresReview;

        Status = status;
        Comment = normalizedComment;
        Source = ChapelAttendanceSource.CbordImport;
        return ChapelImportDecisionResult.Applied;
    }

    public void SetByStaff(ChapelAttendanceStatus status, string? comment)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (comment?.Length > 4000) throw new ArgumentException("Comment is too long.", nameof(comment));
        Status = status;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Source = ChapelAttendanceSource.Staff;
    }
}
