using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.AdvisorPortal;

[Authorize(CampusFlowPermissions.AdvisorPortal.Default)]
public class AdvisorPortalAppService : CampusFlowAppService, IAdvisorPortalAppService
{
    private readonly IRepository<CourseReview, Guid> _reviews;
    private readonly IRepository<CourseReviewSubmission, Guid> _submissions;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly IRepository<AdvisorAssignment, Guid> _advisorAssignments;
    private readonly IStudentInformationSystemCourseRegistrationService _registration;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public AdvisorPortalAppService(
        IRepository<CourseReview, Guid> reviews,
        IRepository<CourseReviewSubmission, Guid> submissions,
        IRepository<StudentProfile, Guid> profiles,
        IRepository<AdvisorAssignment, Guid> advisorAssignments,
        IStudentInformationSystemCourseRegistrationService registration,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _reviews = reviews;
        _submissions = submissions;
        _profiles = profiles;
        _advisorAssignments = advisorAssignments;
        _registration = registration;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<List<AdvisorQueueItemDto>> GetQueueAsync(string? externalTermId = null)
    {
        var pending = await GetVisiblePendingReviewsAsync();
        if (!string.IsNullOrWhiteSpace(externalTermId))
            pending = pending.Where(x => x.ExternalTermId == externalTermId).ToList();
        var profileIds = pending.Select(x => x.StudentProfileId).Distinct().ToArray();
        var profileQuery = await _profiles.GetQueryableAsync();
        var profiles = await AsyncExecuter.ToListAsync(profileQuery.Where(x => profileIds.Contains(x.Id)));
        var profileById = profiles.ToDictionary(x => x.Id);

        return pending
            .GroupBy(x => new { x.StudentProfileId, x.ExternalTermId })
            .Where(group => profileById.ContainsKey(group.Key.StudentProfileId))
            .Select(group =>
            {
                var profile = profileById[group.Key.StudentProfileId];
                return new AdvisorQueueItemDto
                {
                    StudentProfileId = profile.Id,
                    ExternalTermId = group.Key.ExternalTermId,
                    StudentName = profile.DisplayName,
                    StudentId = profile.StudentId,
                    AttendanceType = group.Select(x => x.AttendanceType).FirstOrDefault() ?? string.Empty,
                    PendingCourseCount = group.Count(),
                    WaitingSince = group.Min(x => x.CreationTime)
                };
            })
            .OrderBy(x => x.WaitingSince)
            .ThenBy(x => x.StudentName)
            .ToList();
    }

    public async Task<AdvisorStudentReviewDto> GetStudentReviewAsync(
        Guid studentProfileId, string externalTermId)
    {
        var profile = await _profiles.GetAsync(studentProfileId);
        var query = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.StudentProfileId == studentProfileId && x.ExternalTermId == externalTermId && x.NeedsReview));
        await EnsureVisibleAsync(reviews);

        return new AdvisorStudentReviewDto
        {
            StudentProfileId = profile.Id,
            ExternalTermId = externalTermId,
            StudentName = profile.DisplayName,
            StudentId = profile.StudentId,
            StudentEmail = profile.Email,
            AttendanceType = reviews.Select(x => x.AttendanceType).FirstOrDefault() ?? string.Empty,
            Courses = reviews.OrderBy(x => x.CreationTime).Select(MapCourse).ToList()
        };
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task SubmitAsync(SubmitAdvisorReviewInput input)
    {
        if (CurrentUser.Id is null || string.IsNullOrWhiteSpace(CurrentUser.Email))
            throw new UserFriendlyException("Your advisor identity could not be resolved.");
        if (string.IsNullOrWhiteSpace(input.ExternalTermId))
            throw new UserFriendlyException("The academic term is required.");

        List<CourseReview> reviews;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var query = await _reviews.GetQueryableAsync();
            reviews = await AsyncExecuter.ToListAsync(query.Where(x =>
                x.StudentProfileId == input.StudentProfileId &&
                x.ExternalTermId == input.ExternalTermId && x.NeedsReview));
            await EnsureVisibleAsync(reviews);
            await readUow.CompleteAsync();
        }

        var byId = reviews.ToDictionary(x => x.Id);
        var decisions = input.Courses
            .Where(x => byId.ContainsKey(x.ReviewId))
            .ToList();

        foreach (var decision in decisions)
        {
            var action = decision.Decision?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                await SaveCommentAsync(decision.ReviewId, decision.Comment);
                continue;
            }

            if (action.Equals("Approve", StringComparison.OrdinalIgnoreCase))
            {
                using var approveUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var stored = await _reviews.GetAsync(decision.ReviewId);
                stored.Approve(CurrentUser.Id.Value, Clock.Now, NormalizeComment(decision.Comment));
                await _reviews.UpdateAsync(stored, autoSave: true);
                await approveUow.CompleteAsync();
                continue;
            }

            if (!action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
                throw new UserFriendlyException("A course decision must be Approve, Reject, or left undecided.");

            await RejectAsync(byId[decision.ReviewId], decision.Comment);
        }

        var snapshot = JsonSerializer.Serialize(decisions.Select(x => new
        {
            x.ReviewId,
            Decision = x.Decision?.Trim(),
            Comment = NormalizeComment(x.Comment)
        }));
        using var submissionUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _submissions.InsertAsync(new CourseReviewSubmission(
            GuidGenerator.Create(), CurrentTenant.Id, input.StudentProfileId, input.ExternalTermId,
            CurrentUser.Id.Value, CurrentUser.Email, NormalizeComment(input.OverallComment), snapshot,
            Clock.Now, ReviewEmailStatus.NotConfigured), autoSave: true);
        await submissionUow.CompleteAsync();
    }

    private async Task RejectAsync(CourseReview review, string? comment)
    {
        using (var beginUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var stored = await _reviews.GetAsync(review.Id);
            stored.BeginRejection(CurrentUser.Id!.Value, Clock.Now, NormalizeComment(comment));
            await _reviews.UpdateAsync(stored, autoSave: true);
            await beginUow.CompleteAsync();
        }

        try
        {
            await _registration.RemoveCourseAsync(review.ExternalStudentId, review.ExternalTermId,
                review.ExternalCourseOfferingId, review.ExternalCourseRegistrationId,
                Guid.NewGuid().ToString("D"));
            using var completeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var stored = await _reviews.GetAsync(review.Id);
            stored.CompleteRejection();
            await _reviews.UpdateAsync(stored, autoSave: true);
            await completeUow.CompleteAsync();
        }
        catch (Exception exception)
        {
            using var failedUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var stored = await _reviews.GetAsync(review.Id);
            stored.RecordRemovalFailure(Clock.Now, exception.Message);
            await _reviews.UpdateAsync(stored, autoSave: true);
            await failedUow.CompleteAsync();
            throw new UserFriendlyException(
                "The course could not be removed from Elements. It remains in the advisor queue for retry.",
                innerException: exception);
        }
    }

    private async Task SaveCommentAsync(Guid reviewId, string? comment)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var stored = await _reviews.GetAsync(reviewId);
        stored.RecordComment(NormalizeComment(comment));
        await _reviews.UpdateAsync(stored, autoSave: true);
        await uow.CompleteAsync();
    }

    private async Task<List<CourseReview>> GetVisiblePendingReviewsAsync()
    {
        var query = await _reviews.GetQueryableAsync();
        var pending = await AsyncExecuter.ToListAsync(query.Where(x => x.NeedsReview));
        if (await AuthorizationService.IsGrantedAsync(CampusFlowPermissions.AdvisorPortal.ViewAll))
            return pending;

        var email = CurrentUser.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return [];
        var attendanceTypes = await GetAssignedAttendanceTypesAsync(email);
        return pending.Where(x => attendanceTypes.Contains(x.AttendanceType)).ToList();
    }

    private async Task EnsureVisibleAsync(IReadOnlyCollection<CourseReview> reviews)
    {
        if (reviews.Count == 0)
            throw new UserFriendlyException("There are no pending courses for this student and term.");
        if (await AuthorizationService.IsGrantedAsync(CampusFlowPermissions.AdvisorPortal.ViewAll)) return;

        var email = CurrentUser.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new AbpAuthorizationException("This student is not assigned to the current advisor.");
        var attendanceTypes = await GetAssignedAttendanceTypesAsync(email);
        if (reviews.Any(x => !attendanceTypes.Contains(x.AttendanceType)))
            throw new AbpAuthorizationException("This student is not assigned to the current advisor.");
    }

    private async Task<HashSet<string>> GetAssignedAttendanceTypesAsync(string email)
    {
        var now = Clock.Now;
        var assignments = await _advisorAssignments.GetListAsync(x => x.IsActive &&
            x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now));
        return assignments.Where(x => string.Equals(x.AdvisorEmail, email, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.AttendanceType).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static AdvisorCourseReviewDto MapCourse(CourseReview review)
    {
        CourseSelectionOffering? course = null;
        try { course = JsonSerializer.Deserialize<CourseSelectionOffering>(review.CourseSnapshotJson); }
        catch (JsonException) { }

        return new AdvisorCourseReviewDto
        {
            ReviewId = review.Id,
            CourseCode = course is null ? review.ExternalCourseOfferingId :
                $"{course.Department}{course.CourseCode}",
            Section = course?.Section ?? string.Empty,
            CourseName = course?.CourseName ?? "Course details unavailable",
            Credits = course?.Credits ?? 0,
            InstructorName = course?.InstructorName,
            MeetingDays = course?.MeetingDays,
            Status = review.Status.ToString(),
            AdvisorComment = review.AdvisorComment,
            LastRemovalError = review.LastRemovalError
        };
    }

    private static string? NormalizeComment(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}
