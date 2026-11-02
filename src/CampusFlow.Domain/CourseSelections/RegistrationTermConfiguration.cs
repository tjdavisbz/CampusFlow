using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class RegistrationTermConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string ExternalTermId { get; private set; } = null!;
    public string TermCode { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public DateTime RegistrationOpensAt { get; private set; }
    public DateTime RegistrationClosesAt { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool RequireAdvisorReview { get; private set; }
    public bool EnforceSectionCapacity { get; private set; }
    public bool IsStudentSelectable { get; private set; }
    public bool IsDashboardDefault { get; private set; }
    public string AttendanceTypeMappingsJson { get; private set; } = "[]";
    public string StudentAccessIdsJson { get; private set; } = "[]";
    public bool AllowAllStudentsDuringWindow { get; private set; } = true;
    public string GroupWindowsJson { get; private set; } = "[]";

    protected RegistrationTermConfiguration() { }

    public RegistrationTermConfiguration(Guid id, Guid? tenantId, string externalTermId,
        string termCode, string termName, DateTime opensAt, DateTime closesAt, bool isEnabled,
        bool requireAdvisorReview, bool enforceSectionCapacity, string mappingsJson) : base(id)
    {
        TenantId = tenantId;
        ExternalTermId = externalTermId;
        TermCode = termCode;
        TermName = termName;
        Update(opensAt, closesAt, isEnabled, requireAdvisorReview, enforceSectionCapacity, mappingsJson);
        IsStudentSelectable = true;
    }

    public void Update(DateTime opensAt, DateTime closesAt, bool isEnabled,
        bool requireAdvisorReview, bool enforceSectionCapacity, string mappingsJson)
    {
        if (closesAt <= opensAt) throw new ArgumentException("Registration must close after it opens.");
        RegistrationOpensAt = opensAt;
        RegistrationClosesAt = closesAt;
        IsEnabled = isEnabled;
        RequireAdvisorReview = requireAdvisorReview;
        EnforceSectionCapacity = enforceSectionCapacity;
        AttendanceTypeMappingsJson = mappingsJson;
    }

    public bool IsOpen(DateTime at) => IsEnabled && AllowAllStudentsDuringWindow &&
        RegistrationOpensAt <= at && RegistrationClosesAt >= at;

    public bool IsOpenForStudent(DateTime at, string studentId, string? collegeLevel = null,
        string? attendanceType = null) => IsEnabled &&
        (IsOpen(at) || GetStudentAccessIds().Contains(studentId?.Trim(), StringComparer.Ordinal) ||
         GetGroupWindows().Any(window => window.OpensAt <= at && window.ClosesAt >= at &&
             window.CollegeLevels.Contains(collegeLevel?.Trim(), StringComparer.OrdinalIgnoreCase) &&
             window.AttendanceTypes.Contains(attendanceType?.Trim(), StringComparer.OrdinalIgnoreCase)));

    public IReadOnlyList<CourseSelectionGroupWindow> GetGroupWindows()
    {
        try { return JsonSerializer.Deserialize<CourseSelectionGroupWindow[]>(GroupWindowsJson) ?? []; }
        catch (JsonException) { return []; }
    }

    public void ConfigureGroupWindows(bool allowAllStudentsDuringWindow,
        IEnumerable<CourseSelectionGroupWindow> groupWindows)
    {
        ArgumentNullException.ThrowIfNull(groupWindows);
        var windows = groupWindows.Select(x => new CourseSelectionGroupWindow(
            NormalizeValues(x.CollegeLevels), NormalizeValues(x.AttendanceTypes), x.OpensAt, x.ClosesAt)).ToArray();
        if (windows.Length > 50 || windows.Any(x => x.CollegeLevels.Count == 0 ||
            x.AttendanceTypes.Count == 0 || x.ClosesAt <= x.OpensAt))
            throw new ArgumentException("Each group window needs college levels, attendance types, and a valid time range.",
                nameof(groupWindows));
        AllowAllStudentsDuringWindow = allowAllStudentsDuringWindow;
        GroupWindowsJson = JsonSerializer.Serialize(windows);
    }

    private static string[] NormalizeValues(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalized = values.Select(x => x?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length > 30 || normalized.Any(x => x.Length is < 1 or > 100))
            throw new ArgumentException("A group window contains an invalid college level or attendance type.");
        return normalized;
    }

    public IReadOnlyList<string> GetStudentAccessIds()
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(StudentAccessIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void ConfigureStudentAccess(IEnumerable<string> studentIds)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        var ids = studentIds.Select(x => x?.Trim() ?? string.Empty).ToArray();
        if (ids.Any(x => x.Length is < 1 or > 64 || !x.All(char.IsAsciiDigit)))
            throw new ArgumentException("Student access requires numeric Student IDs of at most 64 digits.", nameof(studentIds));
        StudentAccessIdsJson = JsonSerializer.Serialize(ids.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray());
    }

    public void ConfigureDashboard(bool isStudentSelectable, bool isDashboardDefault)
    {
        IsStudentSelectable = isStudentSelectable;
        IsDashboardDefault = isStudentSelectable && isDashboardDefault;
    }

    public CourseSelectionPolicy CreatePolicy(bool allowOutsideWindow = false) => new(Id, TenantId, TermName, 1,
        RegistrationOpensAt, IsEnabled, RequireAdvisorReview, EnforceSectionCapacity,
        AttendanceTypeMappingsJson, JsonSerializer.Serialize(new CourseSelectionEligibilityRules(
            RegistrationOpensAt: allowOutsideWindow ? null : RegistrationOpensAt,
            RegistrationClosesAt: allowOutsideWindow ? null : RegistrationClosesAt)));
}
