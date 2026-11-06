using System;
using System.Text.Json;
using Volo.Abp.Data;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class AdvisorAssignment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const string GlobalAttendanceType = "Global";
    public Guid? TenantId { get; private set; }
    public string AttendanceType { get; private set; } = null!;
    public string ExternalAdvisorId { get; private set; } = null!;
    public string AdvisorEmail { get; private set; } = null!;
    public string AdvisorDisplayName { get; private set; } = null!;
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public bool IncludeGraduatingStudents { get; private set; } = true;

    protected AdvisorAssignment() { }

    public AdvisorAssignment(Guid id, Guid? tenantId, string attendanceType,
        string externalAdvisorId, string advisorEmail, string advisorDisplayName,
        DateTime effectiveFrom, bool isActive = true, bool includeGraduatingStudents = true) : base(id)
    {
        TenantId = tenantId;
        AttendanceType = attendanceType;
        ExternalAdvisorId = externalAdvisorId;
        AdvisorEmail = advisorEmail;
        AdvisorDisplayName = advisorDisplayName;
        EffectiveFrom = effectiveFrom;
        IsActive = isActive;
        IncludeGraduatingStudents = includeGraduatingStudents;
    }

    public void SetIncludeGraduatingStudents(bool include) => IncludeGraduatingStudents = include;

    public const string PrimaryProgramScopeProperty = "PrimaryProgramScope";

    public PrimaryProgramScope GetPrimaryProgramScope()
    {
        if (!ExtraProperties.ContainsKey(PrimaryProgramScopeProperty)) return PrimaryProgramScope.All;
        var json = this.GetProperty<string>(PrimaryProgramScopeProperty);
        try { return (JsonSerializer.Deserialize<PrimaryProgramScope>(json ?? "null") ?? throw new JsonException()).Normalize(); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new InvalidOperationException("The scheduler primary-program scope needs administrative review.", exception); }
    }

    public void ConfigurePrimaryProgramScope(PrimaryProgramScope scope) =>
        this.SetProperty(PrimaryProgramScopeProperty, JsonSerializer.Serialize(scope.Normalize()));

    public bool IncludesStudent(string attendanceType, bool intendsToGraduate, StudentPrimaryProgram? primaryProgram = null) =>
        GetPrimaryProgramScope().Includes(primaryProgram) &&
        (!intendsToGraduate || IncludeGraduatingStudents) &&
        (string.Equals(AttendanceType, GlobalAttendanceType, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(AttendanceType, attendanceType, StringComparison.OrdinalIgnoreCase));

    public void Retire(DateTime effectiveTo)
    {
        EffectiveTo = effectiveTo;
        IsActive = false;
    }
}
