using System;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Data;

public class CourseSelectionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<CourseSelectionPolicy, Guid> _policies;
    private readonly IRepository<CourseSectionAttendanceTypeMapping, Guid> _sectionMappings;
    private readonly IGuidGenerator _guidGenerator;

    public CourseSelectionDataSeedContributor(
        IRepository<CourseSelectionPolicy, Guid> policies,
        IRepository<CourseSectionAttendanceTypeMapping, Guid> sectionMappings,
        IGuidGenerator guidGenerator)
    {
        _policies = policies;
        _sectionMappings = sectionMappings;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue) return;

        if (await _policies.FindAsync(x =>
                x.TenantId == context.TenantId &&
                x.Name == "Standard Course Selection" &&
                x.Version == 1) is not null)
        {
            await SeedSectionMappingsAsync(context.TenantId.Value);
            return;
        }

        CourseSelectionAttendanceTypeMapping[] attendanceMappings =
        {
            new("Summer*", "Graduate", "Distance Graduate"),
            new("Summer*", "Residential Undergraduate", "Distance Education"),
            new("Summer*", "LEAD", "Distance Education"),
            new("Summer*", "American Indian College", "Distance Education")
        };

        var eligibleTermRules = new
        {
            RequireStudentStatus = true,
            RequireTermDisplayedInPortal = true,
            RequireCourseDisplayedInPortal = true,
            ExcludeFullSections = true
        };

        await _policies.InsertAsync(new CourseSelectionPolicy(
            _guidGenerator.Create(),
            context.TenantId,
            "Standard Course Selection",
            1,
            new DateTime(2026, 1, 1),
            isPublished: true,
            requireAdvisorReview: true,
            enforceSectionCapacity: true,
            JsonSerializer.Serialize(attendanceMappings),
            JsonSerializer.Serialize(eligibleTermRules)));

        await SeedSectionMappingsAsync(context.TenantId.Value);
    }

    private async Task SeedSectionMappingsAsync(Guid tenantId)
    {
        if (await _sectionMappings.AnyAsync(x => x.TenantId == tenantId)) return;

        var effectiveFrom = new DateTime(2026, 1, 1);
        foreach (var (attendanceTypeId, attendanceType, start, end) in NelsonSectionRanges)
        {
            await _sectionMappings.InsertAsync(new CourseSectionAttendanceTypeMapping(
                _guidGenerator.Create(), tenantId, start, end, attendanceTypeId,
                attendanceType, effectiveFrom));
        }
    }

    private static readonly (int AttendanceTypeId, string AttendanceType, int Start, int End)[]
        NelsonSectionRanges =
        [
            (8103, "AIC Online", 159, 159), (8103, "AIC Online", 200, 240), (8103, "AIC Online", 281, 283),
            (6921, "American Indian College", 150, 151), (6921, "American Indian College", 281, 283),
            (3421, "Distance Education", 20, 24), (3421, "Distance Education", 70, 74),
            (3421, "Distance Education", 170, 170), (3421, "Distance Education", 174, 174),
            (3421, "Distance Education", 190, 190), (3421, "Distance Education", 200, 245),
            (3421, "Distance Education", 247, 249), (3421, "Distance Education", 281, 283),
            (3421, "Distance Education", 400, 499),
            (3422, "Distance Graduate", 30, 34), (3422, "Distance Graduate", 90, 90),
            (3422, "Distance Graduate", 94, 94), (3422, "Distance Graduate", 282, 283),
            (3422, "Distance Graduate", 500, 550), (3422, "Distance Graduate", 570, 570),
            (3422, "Distance Graduate", 580, 580),
            (5573, "Dual Credit", 20, 24), (5573, "Dual Credit", 70, 74),
            (5573, "Dual Credit", 170, 170), (5573, "Dual Credit", 174, 179),
            (5573, "Dual Credit", 190, 195), (5573, "Dual Credit", 200, 245),
            (5573, "Dual Credit", 247, 249), (5573, "Dual Credit", 281, 283),
            (5573, "Dual Credit", 400, 499),
            (3427, "Graduate", 30, 34), (3427, "Graduate", 90, 90), (3427, "Graduate", 94, 94),
            (3427, "Graduate", 282, 283), (3427, "Graduate", 500, 550), (3427, "Graduate", 570, 589),
            (8063, "Graduate SOM", 30, 34), (8063, "Graduate SOM", 90, 90),
            (8063, "Graduate SOM", 94, 94), (8063, "Graduate SOM", 282, 283),
            (8063, "Graduate SOM", 500, 550), (8063, "Graduate SOM", 570, 570),
            (8063, "Graduate SOM", 580, 580),
            (6717, "LEAD", 100, 149), (6717, "LEAD", 170, 170), (6717, "LEAD", 174, 174),
            (6717, "LEAD", 190, 190), (6717, "LEAD", 260, 261), (6717, "LEAD", 281, 283),
            (8231, "Oaks Church", 145, 145), (8231, "Oaks Church", 190, 191),
            (8231, "Oaks Church", 200, 204), (8231, "Oaks Church", 210, 211),
            (8231, "Oaks Church", 281, 283), (8231, "Oaks Church", 400, 400),
            (8231, "Oaks Church", 450, 451),
            (5614, "Oaks School of Leadership", 0, 19), (5614, "Oaks School of Leadership", 45, 45),
            (5614, "Oaks School of Leadership", 60, 63), (5614, "Oaks School of Leadership", 74, 74),
            (5614, "Oaks School of Leadership", 100, 149), (5614, "Oaks School of Leadership", 170, 170),
            (5614, "Oaks School of Leadership", 174, 174), (5614, "Oaks School of Leadership", 190, 190),
            (5614, "Oaks School of Leadership", 260, 261), (5614, "Oaks School of Leadership", 281, 283),
            (5614, "Oaks School of Leadership", 300, 399),
            (3434, "Residential Undergraduate", 0, 19), (3434, "Residential Undergraduate", 45, 45),
            (3434, "Residential Undergraduate", 74, 74), (3434, "Residential Undergraduate", 100, 139),
            (3434, "Residential Undergraduate", 170, 174), (3434, "Residential Undergraduate", 190, 190),
            (3434, "Residential Undergraduate", 200, 201), (3434, "Residential Undergraduate", 210, 210),
            (3434, "Residential Undergraduate", 281, 283), (3434, "Residential Undergraduate", 300, 400),
            (3434, "Residential Undergraduate", 450, 450),
            (6974, "SAGU Phoenix", 20, 24), (6974, "SAGU Phoenix", 70, 74),
            (6974, "SAGU Phoenix", 170, 170), (6974, "SAGU Phoenix", 190, 190),
            (6974, "SAGU Phoenix", 200, 245), (6974, "SAGU Phoenix", 247, 249),
            (6974, "SAGU Phoenix", 281, 283), (6974, "SAGU Phoenix", 400, 499),
            (6593, "School of Ministry", 174, 179), (6593, "School of Ministry", 190, 195),
            (6593, "School of Ministry", 200, 240), (6593, "School of Ministry", 281, 283)
        ];
}
