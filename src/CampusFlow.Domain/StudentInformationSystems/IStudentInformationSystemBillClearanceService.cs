using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.StudentInformationSystems;
public interface IStudentInformationSystemBillClearanceService
{
    StudentInformationSystemProvider Provider { get; }
    Task MarkClearedAsync(string externalStudentId,string externalTermId,CancellationToken cancellationToken=default);
}
