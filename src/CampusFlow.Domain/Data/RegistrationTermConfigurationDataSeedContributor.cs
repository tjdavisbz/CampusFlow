using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Data;

public class RegistrationTermConfigurationDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private static readonly string[] NelsonAttendanceTypes =
    [
        "AIC Online", "American Indian College", "Distance Education", "Distance Graduate",
        "Dual Credit", "Graduate", "Graduate SOM", "LEAD", "Oaks Church",
        "Oaks School of Leadership", "Residential Undergraduate", "SAGU Phoenix", "School of Ministry"
    ];

    private readonly IRepository<RegistrationTermConfiguration, Guid> _configurations;
    private readonly IRepository<CourseSelectionPolicy, Guid> _policies;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;

    public RegistrationTermConfigurationDataSeedContributor(
        IRepository<RegistrationTermConfiguration, Guid> configurations,
        IRepository<CourseSelectionPolicy, Guid> policies,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups,
        IGuidGenerator guidGenerator)
    {
        _configurations = configurations;
        _policies = policies;
        _termLookups = termLookups.ToArray();
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue) return;
        var lookup = _termLookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (lookup is null) return;
        var terms = await lookup.GetTermsAsync();
        var configurations = await _configurations.GetListAsync();
        var policy = (await _policies.GetListAsync()).Where(x => x.IsPublished)
            .OrderByDescending(x => x.Version).FirstOrDefault();

        foreach (var term in terms)
        {
            var mappingsJson = CreateMappingsJson(term.DisplayName, policy);
            var existing = configurations.FirstOrDefault(x => x.ExternalTermId == term.ExternalTermId);
            if (existing is not null)
            {
                var mergedMappingsJson = MergeMappings(existing.AttendanceTypeMappingsJson, mappingsJson);
                if (string.Equals(mergedMappingsJson, existing.AttendanceTypeMappingsJson, StringComparison.Ordinal))
                    continue;
                existing.Update(existing.RegistrationOpensAt, existing.RegistrationClosesAt,
                    existing.IsEnabled, existing.RequireAdvisorReview,
                    existing.EnforceSectionCapacity, mergedMappingsJson);
                await _configurations.UpdateAsync(existing);
                continue;
            }
            await _configurations.InsertAsync(new RegistrationTermConfiguration(
                _guidGenerator.Create(), context.TenantId, term.ExternalTermId, term.TermCode,
                term.DisplayName, term.StartDate.Date.AddDays(-61), term.EndDate.Date.AddDays(1).AddTicks(-1),
                false, true, true, mappingsJson));
        }
    }

    private static string CreateMappingsJson(string termName, CourseSelectionPolicy? policy) =>
        JsonSerializer.Serialize(NelsonAttendanceTypes.Select(studentType =>
            new CourseSelectionAttendanceTypeMapping("*", studentType,
                policy?.ResolveCourseAttendanceType(termName, studentType) ??
                ResolveSeededCourseType(termName, studentType))));

    private static string ResolveSeededCourseType(string termName, string studentType)
    {
        if (!termName.StartsWith("Summer", StringComparison.OrdinalIgnoreCase)) return studentType;
        return studentType switch
        {
            "Graduate" => "Distance Graduate",
            "Residential Undergraduate" or "LEAD" or "American Indian College" => "Distance Education",
            _ => studentType
        };
    }

    private static string MergeMappings(string existingJson, string defaultsJson)
    {
        CourseSelectionAttendanceTypeMapping[] existing;
        try { existing = JsonSerializer.Deserialize<CourseSelectionAttendanceTypeMapping[]>(existingJson) ?? []; }
        catch (JsonException) { existing = []; }
        var defaults = JsonSerializer.Deserialize<CourseSelectionAttendanceTypeMapping[]>(defaultsJson) ?? [];
        var missing = defaults.Where(candidate => existing.All(current =>
            !string.Equals(current.StudentAttendanceType, candidate.StudentAttendanceType,
                StringComparison.OrdinalIgnoreCase)));
        var merged = existing.Concat(missing).ToArray();
        return merged.Length == existing.Length ? existingJson : JsonSerializer.Serialize(merged);
    }
}
