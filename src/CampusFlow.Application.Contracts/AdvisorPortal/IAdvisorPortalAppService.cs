using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.AdvisorPortal;

public interface IAdvisorPortalAppService : IApplicationService
{
    Task<List<AdvisorQueueItemDto>> GetQueueAsync(string? externalTermId = null);
    Task<AdvisorStudentReviewDto> GetStudentReviewAsync(Guid studentProfileId, string externalTermId);
    Task SubmitAsync(SubmitAdvisorReviewInput input);
}
