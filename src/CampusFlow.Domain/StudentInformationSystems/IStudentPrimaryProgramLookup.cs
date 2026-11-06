using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.StudentInformationSystems;
public sealed record StudentTermKey(string ExternalStudentId, string ExternalTermId);
public sealed record StudentPrimaryProgram(StudentTermKey Key, int MajorId, string Name, int? DegreeId = null, string? DegreeName = null, int? BlockProgramId = null, string? BlockProgramName = null);
public sealed record StudentProgramChoice(int Id, string Name, int? DegreeId = null, string? DegreeName = null, int? BlockProgramId = null, string? BlockProgramName = null);
public interface IStudentPrimaryProgramLookup
{
    StudentInformationSystemProvider Provider { get; }
    Task<IReadOnlyList<StudentPrimaryProgram>> GetAsync(IReadOnlyCollection<StudentTermKey> keys, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentProgramChoice>> GetChoicesAsync(CancellationToken cancellationToken = default);
}
