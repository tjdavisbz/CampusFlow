using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.CourseSelections;

[Authorize]
public class CourseSelectionAppService : CampusFlowAppService, ICourseSelectionAppService
{
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly IRepository<CourseSelectionPolicy, Guid> _policies;
    private readonly IRepository<AdvisorAssignment, Guid> _advisorAssignments;
    private readonly IRepository<CourseReview, Guid> _reviews;
    private readonly IRepository<CourseSelectionOperation, Guid> _operations;
    private readonly IRepository<CourseSectionAttendanceTypeMapping, Guid> _sectionMappings;
    private readonly IStudentInformationSystemCourseSelectionLookup _lookup;
    private readonly IStudentInformationSystemCourseRegistrationService _registration;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public CourseSelectionAppService(
        IRepository<StudentProfile, Guid> profiles,
        IRepository<CourseSelectionPolicy, Guid> policies,
        IRepository<AdvisorAssignment, Guid> advisorAssignments,
        IRepository<CourseReview, Guid> reviews,
        IRepository<CourseSelectionOperation, Guid> operations,
        IRepository<CourseSectionAttendanceTypeMapping, Guid> sectionMappings,
        IStudentInformationSystemCourseSelectionLookup lookup,
        IStudentInformationSystemCourseRegistrationService registration,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _profiles = profiles;
        _policies = policies;
        _advisorAssignments = advisorAssignments;
        _reviews = reviews;
        _operations = operations;
        _sectionMappings = sectionMappings;
        _lookup = lookup;
        _registration = registration;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<CourseSelectionDto> GetAsync(string externalTermId)
    {
        var profile = await GetCurrentProfileAsync();
        var context = await _lookup.GetContextAsync(profile.ExternalStudentId, externalTermId)
            ?? throw new UserFriendlyException("You are not eligible to select courses for that term.");
        var policy = await GetCurrentPolicyAsync();
        var offerings = await _lookup.GetAvailableOfferingsAsync(externalTermId);
        var sectionMappings = await GetSectionMappingsAsync();
        var registrations = await _lookup.GetRegistrationsAsync(profile.ExternalStudentId, externalTermId);
        var reviewQuery = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(reviewQuery.Where(x =>
            x.StudentProfileId == profile.Id && x.ExternalTermId == externalTermId));

        return new CourseSelectionDto
        {
            ExternalTermId = context.ExternalTermId,
            TermName = context.TermName,
            AttendanceType = context.AttendanceType,
            MaximumAllowedCredits = context.MaximumAllowedCredits,
            SelectedCredits = registrations.Sum(x => x.Credits),
            Offerings = offerings.Select(x => new CourseSelectionOfferingDto
            {
                ExternalOfferingId = x.ExternalOfferingId,
                CourseCode = x.CourseCode,
                Section = x.Section,
                CourseName = x.CourseName,
                Credits = x.Credits,
                CourseAttendanceType = string.Join(", ", GetCourseAttendanceTypes(x.Section, sectionMappings)),
                SeatsRemaining = x.SeatsRemaining,
                InstructorName = x.InstructorName,
                MeetingDays = x.MeetingDays,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                CanSelect = CanSelect(policy, context, x, sectionMappings) &&
                    registrations.All(r => r.ExternalOfferingId != x.ExternalOfferingId)
            }).ToList(),
            Registrations = registrations.Select(x => new CourseSelectionRegistrationDto
            {
                ExternalRegistrationId = x.ExternalRegistrationId,
                ExternalOfferingId = x.ExternalOfferingId,
                CourseCode = x.CourseCode,
                Section = x.Section,
                CourseName = x.CourseName,
                Credits = x.Credits,
                Status = x.RegistrationStatus,
                CanRemove = string.Equals(x.RegistrationStatus, "Unofficial", StringComparison.OrdinalIgnoreCase),
                NeedsReview = reviews.Any(r =>
                    r.ExternalCourseRegistrationId == x.ExternalRegistrationId && r.NeedsReview)
            }).ToList()
        };
    }

    public async Task<List<CourseSelectionTermDto>> GetEligibleTermsAsync()
    {
        var profile = await GetCurrentProfileAsync();
        return (await _lookup.GetEligibleContextsAsync(profile.ExternalStudentId))
            .Select(x => new CourseSelectionTermDto
            {
                ExternalTermId = x.ExternalTermId,
                TermName = x.TermName
            }).ToList();
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<AddCourseSelectionResultDto> AddAsync(AddCourseSelectionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ExternalTermId) ||
            string.IsNullOrWhiteSpace(input.ExternalOfferingId) ||
            !Guid.TryParse(input.IdempotencyKey, out _))
            throw new UserFriendlyException("The course selection request is invalid.");

        StudentProfile profile;
        CourseSelectionPolicy policy;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            profile = await GetCurrentProfileAsync();
            policy = await GetCurrentPolicyAsync();
            await readUow.CompleteAsync();
        }
        var existing = await FindOperationAsync(input.IdempotencyKey);
        if (existing is not null)
            return MapResult(existing);

        var context = await _lookup.GetContextAsync(profile.ExternalStudentId, input.ExternalTermId)
            ?? throw new UserFriendlyException("You are not eligible to select courses for that term.");
        var offering = (await _lookup.GetAvailableOfferingsAsync(input.ExternalTermId))
            .SingleOrDefault(x => x.ExternalOfferingId == input.ExternalOfferingId)
            ?? throw new UserFriendlyException("That course is not available for this term.");
        var registrations = await _lookup.GetRegistrationsAsync(profile.ExternalStudentId, input.ExternalTermId);
        var sectionMappings = await GetSectionMappingsAsync();

        if (!CanSelect(policy, context, offering, sectionMappings))
            throw new UserFriendlyException("That course is not available for your attendance type.");
        if (registrations.Any(x => x.ExternalOfferingId == input.ExternalOfferingId))
            throw new UserFriendlyException("That course is already on your schedule.");
        if (registrations.Sum(x => x.Credits) + offering.Credits > context.MaximumAllowedCredits)
            throw new UserFriendlyException("Adding that course would exceed your allowed credit limit.");

        var snapshot = JsonSerializer.Serialize(offering);
        var operation = new CourseSelectionOperation(GuidGenerator.Create(), CurrentTenant.Id, profile.Id,
            profile.ExternalStudentId, input.ExternalTermId, input.ExternalOfferingId,
            input.IdempotencyKey, snapshot);
        operation.RecordAttempt(Clock.Now);
        await SaveNewOperationAsync(operation);

        string? externalRegistrationId = null;
        try
        {
            externalRegistrationId = await _registration.AddUnofficialCourseAsync(
                profile.ExternalStudentId, input.ExternalTermId, input.ExternalOfferingId,
                input.IdempotencyKey);
            await RecordExternalRegistrationAsync(operation.Id, externalRegistrationId);

            var assignment = await GetAdvisorAssignmentAsync(context.AttendanceType);
            var review = new CourseReview(GuidGenerator.Create(), CurrentTenant.Id, profile.Id,
                profile.ExternalStudentId, input.ExternalTermId, input.ExternalOfferingId,
                externalRegistrationId, context.AttendanceType, snapshot,
                assignment?.ExternalAdvisorId, assignment?.AdvisorEmail, policy.RequireAdvisorReview);
            await CompleteOperationAsync(operation.Id, review);
            return MapResult(await GetOperationAsync(operation.Id));
        }
        catch (CourseRegistrationValidationException exception)
        {
            await RecordFailureAsync(operation.Id, null, exception.Message, false);
            throw new UserFriendlyException(exception.Message, innerException: exception);
        }
        catch (Exception exception)
        {
            var mayHaveRegistered = exception is not CourseRegistrationException registrationException ||
                                    registrationException.ExternalRegistrationMayHaveCompleted;
            await RecordFailureAsync(operation.Id, externalRegistrationId, exception.Message, mayHaveRegistered);
            throw new UserFriendlyException(
                mayHaveRegistered
                    ? "We could not finish recording this course selection. No further action is needed right now; the request has been saved for review."
                    : "We could not add this course. No course was added; please try again shortly.",
                innerException: exception);
        }
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task RemoveAsync(RemoveCourseSelectionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ExternalTermId) ||
            string.IsNullOrWhiteSpace(input.ExternalOfferingId) ||
            string.IsNullOrWhiteSpace(input.ExternalRegistrationId) ||
            !Guid.TryParse(input.IdempotencyKey, out _))
            throw new UserFriendlyException("The course removal request is invalid.");

        StudentProfile profile;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            profile = await GetCurrentProfileAsync();
            await readUow.CompleteAsync();
        }

        var registration = (await _lookup.GetRegistrationsAsync(
                profile.ExternalStudentId, input.ExternalTermId))
            .SingleOrDefault(x => x.ExternalRegistrationId == input.ExternalRegistrationId &&
                                  x.ExternalOfferingId == input.ExternalOfferingId &&
                                  x.EffectiveWithdrawDate is null)
            ?? throw new UserFriendlyException("That course is no longer on your schedule.");
        if (!string.Equals(registration.RegistrationStatus, "Unofficial", StringComparison.OrdinalIgnoreCase))
            throw new UserFriendlyException("Official courses cannot be removed through Course Selection.");

        CourseReview? review;
        using (var reviewUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            review = await _reviews.FindAsync(x => x.StudentProfileId == profile.Id &&
                x.ExternalCourseRegistrationId == input.ExternalRegistrationId);
            if (review is not null)
            {
                review.BeginStudentRemoval(Clock.Now);
                await _reviews.UpdateAsync(review, autoSave: true);
            }
            await reviewUow.CompleteAsync();
        }

        try
        {
            await _registration.RemoveCourseAsync(profile.ExternalStudentId, input.ExternalTermId,
                input.ExternalOfferingId, input.ExternalRegistrationId, input.IdempotencyKey);
            if (review is not null)
            {
                using var completeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var stored = await _reviews.GetAsync(review.Id);
                stored.CompleteStudentRemoval();
                await _reviews.UpdateAsync(stored, autoSave: true);
                await completeUow.CompleteAsync();
            }
        }
        catch (Exception exception)
        {
            if (review is not null)
            {
                using var failedUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var stored = await _reviews.GetAsync(review.Id);
                stored.RecordRemovalFailure(Clock.Now, exception.Message);
                await _reviews.UpdateAsync(stored, autoSave: true);
                await failedUow.CompleteAsync();
            }
            throw new UserFriendlyException(
                "We could not remove this course. It remains on your schedule; please try again shortly.",
                innerException: exception);
        }
    }

    private async Task<StudentProfile> GetCurrentProfileAsync()
    {
        if (CurrentUser.Id is null)
            throw new UserFriendlyException("You must be signed in to select courses.");
        return await _profiles.FindAsync(x => x.UserId == CurrentUser.Id.Value)
            ?? throw new UserFriendlyException("Your student profile could not be found.");
    }

    private async Task<CourseSelectionPolicy> GetCurrentPolicyAsync()
    {
        var now = Clock.Now;
        var query = await _policies.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(query
                   .Where(x => x.IsPublished && x.EffectiveFrom <= now &&
                               (x.EffectiveTo == null || x.EffectiveTo > now))
                   .OrderByDescending(x => x.Version))
               ?? throw new UserFriendlyException("Course Selection is not currently configured.");
    }

    private async Task<AdvisorAssignment?> GetAdvisorAssignmentAsync(string attendanceType)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var now = Clock.Now;
        var query = await _advisorAssignments.GetQueryableAsync();
        var assignment = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x =>
            x.IsActive && x.AttendanceType == attendanceType && x.EffectiveFrom <= now &&
            (x.EffectiveTo == null || x.EffectiveTo > now)).OrderByDescending(x => x.EffectiveFrom));
        await uow.CompleteAsync();
        return assignment;
    }

    private async Task<IReadOnlyList<CourseSectionAttendanceTypeMapping>> GetSectionMappingsAsync()
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var now = Clock.Now;
        var query = await _sectionMappings.GetQueryableAsync();
        var mappings = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.IsActive && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now)));
        await uow.CompleteAsync();
        return mappings;
    }

    private static bool CanSelect(CourseSelectionPolicy policy, CourseSelectionContext context,
        CourseSelectionOffering offering, IReadOnlyList<CourseSectionAttendanceTypeMapping> mappings)
    {
        var attendanceTypes = GetCourseAttendanceTypes(offering.Section, mappings);
        return attendanceTypes.Count > 0 && policy.CanSelect(context, offering, attendanceTypes);
    }

    private static IReadOnlyList<string> GetCourseAttendanceTypes(string section,
        IReadOnlyList<CourseSectionAttendanceTypeMapping> mappings)
    {
        if (!int.TryParse(section, out var sectionNumber)) return [];
        return mappings.Where(x => sectionNumber >= x.SectionStart && sectionNumber <= x.SectionEnd)
            .Select(x => x.AttendanceType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<CourseSelectionOperation?> FindOperationAsync(string idempotencyKey)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var operation = await _operations.FindAsync(x => x.IdempotencyKey == idempotencyKey);
        await uow.CompleteAsync();
        return operation;
    }

    private async Task<CourseSelectionOperation> GetOperationAsync(Guid id)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var operation = await _operations.GetAsync(id);
        await uow.CompleteAsync();
        return operation;
    }

    private async Task SaveNewOperationAsync(CourseSelectionOperation operation)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _operations.InsertAsync(operation, autoSave: true);
        await uow.CompleteAsync();
    }

    private async Task RecordExternalRegistrationAsync(Guid operationId, string registrationId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var operation = await _operations.GetAsync(operationId);
        operation.RecordExternalRegistration(registrationId);
        await _operations.UpdateAsync(operation, autoSave: true);
        await uow.CompleteAsync();
    }

    private async Task CompleteOperationAsync(Guid operationId, CourseReview review)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var operation = await _operations.GetAsync(operationId);
        await _reviews.InsertAsync(review, autoSave: true);
        operation.Complete(review.Id);
        await _operations.UpdateAsync(operation, autoSave: true);
        await uow.CompleteAsync();
    }

    private async Task RecordFailureAsync(Guid operationId, string? registrationId, string error,
        bool externalRegistrationMayHaveCompleted)
    {
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var operation = await _operations.GetAsync(operationId);
            if (registrationId is not null && operation.ExternalCourseRegistrationId is null)
                operation.RecordExternalRegistration(registrationId);
            operation.Fail(error, externalRegistrationMayHaveCompleted);
            await _operations.UpdateAsync(operation, autoSave: true);
            await uow.CompleteAsync();
        }
        catch
        {
            // Preserve the original registration error. The durable pending operation remains available.
        }
    }

    private static AddCourseSelectionResultDto MapResult(CourseSelectionOperation operation) => new()
    {
        OperationId = operation.Id,
        CourseReviewId = operation.CourseReviewId,
        ExternalRegistrationId = operation.ExternalCourseRegistrationId,
        Status = operation.Status.ToString()
    };
}
