using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.StudentInformationSystems;
namespace CampusFlow.CourseSelections;

// Each populated dimension narrows access; all populated dimensions must match.
public sealed record PrimaryProgramScope(IReadOnlyList<int> MajorIds, IReadOnlyList<int> DegreeIds, IReadOnlyList<int> ProgramIds)
{
    public static PrimaryProgramScope All { get; } = new([], [], []);
    public bool IsRestricted => MajorIds.Count > 0 || DegreeIds.Count > 0 || ProgramIds.Count > 0;
    public bool Includes(StudentPrimaryProgram? primary) => !IsRestricted || primary is not null &&
        (MajorIds.Count == 0 || MajorIds.Contains(primary.MajorId)) &&
        (DegreeIds.Count == 0 || primary.DegreeId.HasValue && DegreeIds.Contains(primary.DegreeId.Value)) &&
        (ProgramIds.Count == 0 || primary.BlockProgramId.HasValue && ProgramIds.Contains(primary.BlockProgramId.Value));

    public PrimaryProgramScope Normalize()
    {
        if (MajorIds is null || DegreeIds is null || ProgramIds is null ||
            new[] { MajorIds, DegreeIds, ProgramIds }.Any(x => x.Count > 1000 || x.Any(id => id <= 0)))
            throw new ArgumentException("Choose valid primary-program, major and degree identifiers.");
        return new(MajorIds.Distinct().Order().ToArray(), DegreeIds.Distinct().Order().ToArray(), ProgramIds.Distinct().Order().ToArray());
    }
}
