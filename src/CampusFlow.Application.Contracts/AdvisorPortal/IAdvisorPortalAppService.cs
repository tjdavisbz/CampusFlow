using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CampusFlow.AdvisorPortal;

public interface IAdvisorPortalAppService : IApplicationService
{
    Task<bool> HasAccessAsync();
    Task<List<AdvisorQueueItemDto>> GetQueueAsync(string? externalTermId = null);
    Task<AdvisorStudentReviewDto> GetStudentReviewAsync(Guid studentProfileId, string externalTermId);
    Task<AdvisorReviewEmailDto> PreviewEmailAsync(SubmitAdvisorReviewInput input);
    Task<Guid> SubmitAsync(SubmitAdvisorReviewInput input);
    Task EnsureStudentNoteAsync(Guid submissionId);
    Task<AdvisorReviewEmailDto> GetSubmissionEmailAsync(Guid submissionId);
}
