using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.CourseSelections;

public class CourseSelectionPolicy : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Version { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsPublished { get; private set; }
    public bool RequireAdvisorReview { get; private set; }
    public bool EnforceSectionCapacity { get; private set; }
    public string AttendanceTypeMappingsJson { get; private set; } = "[]";
    public string EligibleTermRulesJson { get; private set; } = "{}";

    protected CourseSelectionPolicy() { }

    public CourseSelectionPolicy(Guid id, Guid? tenantId, string name, int version,
        DateTime effectiveFrom, bool isPublished, bool requireAdvisorReview,
        bool enforceSectionCapacity, string attendanceTypeMappingsJson,
        string eligibleTermRulesJson) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Version = version;
        EffectiveFrom = effectiveFrom;
        IsPublished = isPublished;
        RequireAdvisorReview = requireAdvisorReview;
        EnforceSectionCapacity = enforceSectionCapacity;
        AttendanceTypeMappingsJson = attendanceTypeMappingsJson;
        EligibleTermRulesJson = eligibleTermRulesJson;
    }

    public void Retire(DateTime effectiveTo)
    {
        EffectiveTo = effectiveTo;
        IsPublished = false;
    }

    public bool CanSelect(CourseSelectionContext context, CourseSelectionOffering offering)
        => CanSelect(context, offering, [offering.CourseAttendanceType]);

    public bool CanSelect(CourseSelectionContext context, CourseSelectionOffering offering,
        IReadOnlyCollection<string> courseAttendanceTypes)
        => CanSelect(context, offering, courseAttendanceTypes, DateTime.UtcNow);

    public bool CanSelect(CourseSelectionContext context, CourseSelectionOffering offering,
        IReadOnlyCollection<string> courseAttendanceTypes, DateTime at)
    {
        if (!GetEligibilityRules().IsOpen(at)) return false;
        if (!string.Equals(context.ExternalTermId, offering.ExternalTermId, StringComparison.Ordinal))
            return false;
        if (EnforceSectionCapacity && offering.SeatsRemaining <= 0)
            return false;

        var courseAttendanceType = ResolveCourseAttendanceType(context.TermName, context.AttendanceType);
        return courseAttendanceTypes.Any(x => string.Equals(courseAttendanceType, x,
            StringComparison.OrdinalIgnoreCase));
    }

    public CourseSelectionEligibilityRules GetEligibilityRules()
    {
        try
        {
            return JsonSerializer.Deserialize<CourseSelectionEligibilityRules>(EligibleTermRulesJson)
                   ?? new CourseSelectionEligibilityRules();
        }
        catch (JsonException)
        {
            return new CourseSelectionEligibilityRules();
        }
    }

    public string ResolveCourseAttendanceType(string termName, string studentAttendanceType)
    {
        IReadOnlyList<CourseSelectionAttendanceTypeMapping> mappings;
        try
        {
            mappings = JsonSerializer.Deserialize<CourseSelectionAttendanceTypeMapping[]>(
                AttendanceTypeMappingsJson) ?? [];
        }
        catch (JsonException)
        {
            mappings = [];
        }

        foreach (var mapping in mappings)
        {
            if (Matches(mapping.TermPattern, termName) &&
                string.Equals(mapping.StudentAttendanceType, studentAttendanceType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return mapping.CourseAttendanceType;
            }
        }
        return studentAttendanceType;
    }

    private static bool Matches(string pattern, string value)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }
}
