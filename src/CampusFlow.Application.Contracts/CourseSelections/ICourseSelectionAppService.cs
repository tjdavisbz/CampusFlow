using System.Threading.Tasks;
using System.Collections.Generic;
using Volo.Abp.Application.Services;

namespace CampusFlow.CourseSelections;

public interface ICourseSelectionAppService : IApplicationService
{
    Task<CourseSelectionDto> GetAsync(string externalTermId);
    Task<List<CourseSelectionTermDto>> GetEligibleTermsAsync();
    Task<AddCourseSelectionResultDto> AddAsync(AddCourseSelectionInput input);
    Task RemoveAsync(RemoveCourseSelectionInput input);
}
