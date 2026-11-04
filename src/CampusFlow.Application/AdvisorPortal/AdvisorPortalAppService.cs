using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.AdvisorPortal;

[Authorize]
public class AdvisorPortalAppService : CampusFlowAppService, IAdvisorPortalAppService
{
    private readonly IRepository<CourseReview, Guid> _reviews;
    private readonly IRepository<CourseReviewSubmission, Guid> _submissions;
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly IRepository<AdvisorAssignment, Guid> _advisorAssignments;
    private readonly IRepository<StudentGraduationIntent, Guid> _graduationIntents;
    private readonly IStudentInformationSystemCourseRegistrationService _registration;
    private readonly IStudentInformationSystemCourseSelectionLookup _courseLookup;
    private readonly IStudentInformationSystemStudentNotesService _studentNotes;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public AdvisorPortalAppService(
        IRepository<CourseReview, Guid> reviews,
        IRepository<CourseReviewSubmission, Guid> submissions,
        IRepository<StudentProfile, Guid> profiles,
        IRepository<AdvisorAssignment, Guid> advisorAssignments,
        IRepository<StudentGraduationIntent, Guid> graduationIntents,
        IStudentInformationSystemCourseRegistrationService registration,
        IStudentInformationSystemCourseSelectionLookup courseLookup,
        IStudentInformationSystemStudentNotesService studentNotes,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _reviews = reviews;
        _submissions = submissions;
        _profiles = profiles;
        _advisorAssignments = advisorAssignments;
        _graduationIntents = graduationIntents;
        _registration = registration;
        _courseLookup = courseLookup;
        _studentNotes = studentNotes;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<List<AdvisorQueueItemDto>> GetQueueAsync(string? externalTermId = null)
    {
        if (!await HasAccessAsync())
            throw new AbpAuthorizationException("This account has no active advisor assignment.");
        var pending = await GetVisiblePendingReviewsAsync(externalTermId);
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

    public async Task<bool> HasAccessAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentUser.Email)) return false;
        return (await GetActiveAssignmentsAsync(CurrentUser.Email)).Count > 0;
    }

    public async Task<AdvisorStudentReviewDto> GetStudentReviewAsync(
        Guid studentProfileId, string externalTermId)
    {
        var profile = await _profiles.GetAsync(studentProfileId);
        var query = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.StudentProfileId == studentProfileId && x.ExternalTermId == externalTermId && x.NeedsReview));
        var registrations = await _courseLookup.GetRegistrationsAsync(profile.ExternalStudentId, externalTermId);
        var activeIds = registrations.Where(x => x.EffectiveWithdrawDate is null)
            .Select(x => x.ExternalRegistrationId).ToHashSet(StringComparer.Ordinal);
        reviews = CourseReviewReconciliation.KeepActivePending(reviews, activeIds);
        await EnsureVisibleAsync(reviews);
        var pendingIds = reviews.Select(x => x.ExternalCourseRegistrationId).ToHashSet(StringComparer.Ordinal);
        var activeCourses = registrations.Where(x => x.EffectiveWithdrawDate is null &&
            !pendingIds.Contains(x.ExternalRegistrationId));

        return new AdvisorStudentReviewDto
        {
            StudentProfileId = profile.Id,
            ExternalTermId = externalTermId,
            StudentName = profile.DisplayName,
            StudentId = profile.StudentId,
            StudentEmail = profile.Email,
            AttendanceType = reviews.Select(x => x.AttendanceType).FirstOrDefault() ?? string.Empty,
            ExistingCourses = activeCourses.Select(x => new AdvisorScheduledCourseDto
            {
                CourseCode = CourseSelectionCourseCode.Format(x.Department, x.CourseCode, x.CourseType),
                Section = x.Section,
                CourseName = x.CourseName,
                Credits = x.Credits,
                Status = x.RegistrationStatus
            }).ToList(),
            Courses = reviews.OrderBy(x => x.CreationTime).Select(MapCourse).ToList()
        };
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<AdvisorReviewEmailDto> PreviewEmailAsync(SubmitAdvisorReviewInput input)
    {
        if (!await HasAccessAsync()) throw new AbpAuthorizationException("No active advisor assignment.");
        var profile = await _profiles.GetAsync(input.StudentProfileId);
        var query = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.StudentProfileId == input.StudentProfileId && x.ExternalTermId == input.ExternalTermId));
        reviews = await KeepActiveReviewsAsync(profile, input.ExternalTermId, reviews);
        await EnsureVisibleAsync(reviews.Where(x => x.NeedsReview).ToArray());
        ValidateSubmittedDecisions(input, reviews.Where(x => x.NeedsReview).ToArray());
        return await BuildEmailAsync(profile, input.ExternalTermId, reviews,
            input.OverallComment, input.Courses);
    }

    public async Task<Guid> SubmitAsync(SubmitAdvisorReviewInput input)
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
            var profile = await _profiles.GetAsync(input.StudentProfileId);
            reviews = await KeepActiveReviewsAsync(profile, input.ExternalTermId, reviews);
            await EnsureVisibleAsync(reviews);
            await readUow.CompleteAsync();
        }

        var byId = reviews.ToDictionary(x => x.Id);
        ValidateSubmittedDecisions(input, reviews);
        var decisions = input.Courses.ToList();

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
        var submission = new CourseReviewSubmission(
            GuidGenerator.Create(), CurrentTenant.Id, input.StudentProfileId, input.ExternalTermId,
            CurrentUser.Id.Value, CurrentUser.Email, NormalizeComment(input.OverallComment), snapshot,
            Clock.Now, ReviewEmailStatus.Pending);
        await _submissions.InsertAsync(submission, autoSave: true);
        await submissionUow.CompleteAsync();
        return submission.Id;
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task EnsureStudentNoteAsync(Guid submissionId)
    {
        if (CurrentUser.Id is null || !await HasAccessAsync())
            throw new AbpAuthorizationException("This review is not available to this account.");
        var submission = await _submissions.GetAsync(submissionId);
        if (submission.SubmittedByUserId != CurrentUser.Id)
            throw new AbpAuthorizationException("This review is not available to this account.");
        if (string.IsNullOrWhiteSpace(submission.OverallComment) || submission.StudentNoteWrittenAt.HasValue)
            return;

        var profile = await _profiles.GetAsync(submission.StudentProfileId);
        var context = await _courseLookup.GetContextAsync(profile.ExternalStudentId, submission.ExternalTermId);
        await _studentNotes.EnsureSchedulerCommentAsync(profile.ExternalStudentId,
            context?.TermName ?? submission.ExternalTermId, submission.OverallComment, submission.SubmittedAt);

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var stored = await _submissions.GetAsync(submissionId);
        stored.RecordStudentNoteWritten(Clock.Now);
        await _submissions.UpdateAsync(stored, autoSave: true);
        await uow.CompleteAsync();
    }

    public async Task<AdvisorReviewEmailDto> GetSubmissionEmailAsync(Guid submissionId)
    {
        var submission = await _submissions.GetAsync(submissionId);
        if (CurrentUser.Id != submission.SubmittedByUserId || !await HasAccessAsync())
            throw new AbpAuthorizationException("This review message is not available to this account.");
        var profile = await _profiles.GetAsync(submission.StudentProfileId);
        var query = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.StudentProfileId == submission.StudentProfileId &&
            x.ExternalTermId == submission.ExternalTermId));
        reviews = await KeepActiveReviewsAsync(profile, submission.ExternalTermId, reviews);
        if (reviews.Count == 0) throw new UserFriendlyException("No course reviews were found for this message.");
        await EnsureAssignedToStudentAsync(reviews);
        var email = await BuildEmailAsync(profile, submission.ExternalTermId, reviews,
            submission.OverallComment, []);
        email.SubmissionId = submissionId;
        email.EmailStatus = submission.EmailStatus.ToString();
        return email;
    }

    private static void ValidateSubmittedDecisions(SubmitAdvisorReviewInput input,
        IReadOnlyCollection<CourseReview> pending)
    {
        if (input.Courses.Count != pending.Count ||
            input.Courses.Select(x => x.ReviewId).Distinct().Count() != pending.Count ||
            input.Courses.Any(x => pending.All(r => r.Id != x.ReviewId)))
            throw new UserFriendlyException("The pending course list changed. Reload and review it again.");
        if (input.Courses.Any(x => !string.IsNullOrWhiteSpace(x.Decision) &&
            !x.Decision.Equals("Approve", StringComparison.OrdinalIgnoreCase) &&
            !x.Decision.Equals("Reject", StringComparison.OrdinalIgnoreCase)))
            throw new UserFriendlyException("A course decision must be Approve, Reject, or left undecided.");
    }

    private async Task<AdvisorReviewEmailDto> BuildEmailAsync(StudentProfile profile,
        string externalTermId, IReadOnlyCollection<CourseReview> reviews, string? overallComment,
        IReadOnlyCollection<AdvisorCourseDecisionInput> proposed)
    {
        var decisions = proposed.ToDictionary(x => x.ReviewId);
        var context = await _courseLookup.GetContextAsync(profile.ExternalStudentId, externalTermId);
        return new AdvisorReviewEmailDto
        {
            StudentName = profile.DisplayName,
            StudentEmail = profile.Email,
            ExternalTermId = externalTermId,
            TermName = context?.TermName ?? externalTermId,
            OverallComment = NormalizeComment(overallComment),
            Courses = reviews.Where(x => x.Status != CourseReviewStatus.RemovedByStudent)
                .OrderBy(x => x.CreationTime).Select(review =>
            {
                var course = MapCourse(review);
                decisions.TryGetValue(review.Id, out var proposedDecision);
                var proposedStatus = proposedDecision?.Decision?.Trim();
                return new AdvisorEmailCourseDto
                {
                    CourseCode = course.CourseCode,
                    Section = course.Section,
                    CourseName = course.CourseName,
                    Status = proposedStatus?.Equals("Approve", StringComparison.OrdinalIgnoreCase) == true
                        ? "Approved" : proposedStatus?.Equals("Reject", StringComparison.OrdinalIgnoreCase) == true
                        ? "Rejected" : review.Status == CourseReviewStatus.Approved ? "Approved"
                        : review.Status == CourseReviewStatus.Rejected ? "Rejected" : "Still pending",
                    AdvisorComment = NormalizeComment(proposedDecision?.Comment) ?? review.AdvisorComment
                };
            }).ToList()
        };
    }

    private async Task EnsureAssignedToStudentAsync(IReadOnlyCollection<CourseReview> reviews)
    {
        var assignments = await GetActiveAssignmentsAsync(CurrentUser.Email);
        var first = reviews.First();
        var graduation = await _graduationIntents.FindAsync(x =>
            x.StudentProfileId == first.StudentProfileId && x.ExternalTermId == first.ExternalTermId);
        if (reviews.Any(x => !CanSee(x, assignments, graduation?.IntendsToGraduate == true)))
            throw new AbpAuthorizationException("This student is not assigned to the current advisor.");
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

    private async Task<List<CourseReview>> GetVisiblePendingReviewsAsync(string? externalTermId)
    {
        externalTermId = string.IsNullOrWhiteSpace(externalTermId) ? null : externalTermId;
        var query = await _reviews.GetQueryableAsync();
        var pending = await AsyncExecuter.ToListAsync(query.Where(x => x.NeedsReview &&
            (externalTermId == null || x.ExternalTermId == externalTermId)));
        if (pending.Count == 0) return pending;
        var activeIds = await _courseLookup.GetActiveRegistrationIdsAsync(
            pending.Select(x => x.ExternalCourseRegistrationId).Distinct().ToArray());
        pending = CourseReviewReconciliation.KeepActivePending(pending, activeIds);
        var assignments = await GetActiveAssignmentsAsync(CurrentUser.Email);
        var profileIds = pending.Select(x => x.StudentProfileId).Distinct().ToArray();
        var graduationQuery = await _graduationIntents.GetQueryableAsync();
        var graduating = (await AsyncExecuter.ToListAsync(graduationQuery.Where(x =>
            profileIds.Contains(x.StudentProfileId) && x.IntendsToGraduate)))
            .Select(x => (x.StudentProfileId, x.ExternalTermId)).ToHashSet();
        return pending.Where(x => CanSee(x, assignments,
            graduating.Contains((x.StudentProfileId, x.ExternalTermId)))).ToList();
    }

    private static bool CanSee(CourseReview review, IReadOnlyCollection<AdvisorAssignment> assignments,
        bool graduating) => assignments.Any(x => x.IncludesStudent(review.AttendanceType, graduating));

    private async Task<List<CourseReview>> KeepActiveReviewsAsync(StudentProfile profile,
        string externalTermId, IEnumerable<CourseReview> reviews)
    {
        var registrations = await _courseLookup.GetRegistrationsAsync(profile.ExternalStudentId, externalTermId);
        var activeIds = registrations.Where(x => x.EffectiveWithdrawDate is null)
            .Select(x => x.ExternalRegistrationId).ToHashSet(StringComparer.Ordinal);
        // A rejected course has already been removed from Elements but must remain in
        // the advisor's results email. Only stale pending courses disappear.
        return CourseReviewReconciliation.KeepActivePending(reviews, activeIds);
    }

    private async Task EnsureVisibleAsync(IReadOnlyCollection<CourseReview> reviews)
    {
        if (reviews.Count == 0)
            throw new UserFriendlyException("There are no pending courses for this student and term.");
        await EnsureAssignedToStudentAsync(reviews);
    }

    private async Task<List<AdvisorAssignment>> GetActiveAssignmentsAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return [];
        var now = Clock.Now;
        var assignments = await _advisorAssignments.GetListAsync(x => x.IsActive &&
            x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now));
        return assignments.Where(x => string.Equals(x.AdvisorEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
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
                CourseSelectionCourseCode.Format(course.Department, course.CourseCode, course.CourseType),
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
