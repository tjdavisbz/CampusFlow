using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Chapel;

/// <summary>A student's request, not an approved excuse or an attendance decision.</summary>
public class ChapelExcuseRequest : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int TermCalendarId { get; private set; }
    public int StudentUid { get; private set; }
    public string StudentId { get; private set; } = "";
    public string StudentName { get; private set; } = "";
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string ExcuseType { get; private set; } = "";
    public string Reason { get; private set; } = "";

    protected ChapelExcuseRequest() { }

    public ChapelExcuseRequest(Guid id, Guid? tenantId, int termCalendarId, int studentUid,
        string studentId, string studentName, DateOnly startDate, DateOnly endDate,
        string excuseType, string reason) : base(id)
    {
        if (termCalendarId <= 0) throw new ArgumentOutOfRangeException(nameof(termCalendarId));
        if (studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        if (string.IsNullOrWhiteSpace(studentId) || studentId.Length > 64)
            throw new ArgumentException("A student ID is required.", nameof(studentId));
        if (string.IsNullOrWhiteSpace(studentName) || studentName.Length > 256)
            throw new ArgumentException("A student name is required.", nameof(studentName));
        if (endDate < startDate || endDate.DayNumber - startDate.DayNumber > 2)
            throw new ArgumentException("Choose one to three consecutive calendar days.", nameof(endDate));
        if (excuseType is not ("Work" or "MedicalIssue" or "Other"))
            throw new ArgumentException("Choose a valid excuse type.", nameof(excuseType));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new ArgumentException("Enter a reason of up to 2,000 characters.", nameof(reason));

        TenantId = tenantId;
        TermCalendarId = termCalendarId;
        StudentUid = studentUid;
        StudentId = studentId.Trim();
        StudentName = studentName.Trim();
        StartDate = startDate;
        EndDate = endDate;
        ExcuseType = excuseType;
        Reason = reason.Trim();
    }
}
