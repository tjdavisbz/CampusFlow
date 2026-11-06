using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.BillApprovals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.CourseSelections;

[Authorize]
public class CourseSelectionAppService : CampusFlowAppService, ICourseSelectionAppService
{
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _termConfigurations;
    private readonly IRepository<AdvisorAssignment, Guid> _advisorAssignments;
    private readonly IRepository<CourseReview, Guid> _reviews;
    private readonly IRepository<CourseSelectionOperation, Guid> _operations;
    private readonly IRepository<StudentGraduationIntent, Guid> _graduationIntents;
    private readonly IRepository<CourseSectionAttendanceTypeMapping, Guid> _sectionMappings;
    private readonly IStudentInformationSystemCourseSelectionLookup _lookup;
    private readonly IStudentInformationSystemCourseRegistrationService _registration;
    private readonly IReadOnlyCollection<IStudentInformationSystemDegreeAuditLookup> _degreeAuditLookups;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<CourseSelectionAppService> _logger;
    private readonly IBillApprovalCompletionLookup _completion;

    private readonly IReadOnlyCollection<IStudentPrimaryProgramLookup> _programLookups;

    public CourseSelectionAppService(
        IRepository<StudentProfile, Guid> profiles,
        ICurrentStudentView currentStudentView,
        IRepository<RegistrationTermConfiguration, Guid> termConfigurations,
        IRepository<AdvisorAssignment, Guid> advisorAssignments,
        IRepository<CourseReview, Guid> reviews,
        IRepository<CourseSelectionOperation, Guid> operations,
        IRepository<StudentGraduationIntent, Guid> graduationIntents,
        IRepository<CourseSectionAttendanceTypeMapping, Guid> sectionMappings,
        IStudentInformationSystemCourseSelectionLookup lookup,
        IStudentInformationSystemCourseRegistrationService registration,
        IEnumerable<IStudentInformationSystemDegreeAuditLookup> degreeAuditLookups,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<CourseSelectionAppService> logger, IBillApprovalCompletionLookup completion,
        IEnumerable<IStudentPrimaryProgramLookup>? programLookups = null)
    {
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _termConfigurations = termConfigurations;
        _advisorAssignments = advisorAssignments;
        _reviews = reviews;
        _operations = operations;
        _graduationIntents = graduationIntents;
        _sectionMappings = sectionMappings;
        _lookup = lookup;
        _registration = registration;
        _degreeAuditLookups = degreeAuditLookups.ToList();
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
        _completion = completion;
        _programLookups = programLookups?.ToArray() ?? [];
    }

    public async Task<CourseSelectionDto> GetAsync(string externalTermId)
    {
        var profile = await GetCurrentProfileAsync();
        var locked = await _completion.IsApprovedAsync(profile, externalTermId);
        var context = await _lookup.GetContextAsync(profile.ExternalStudentId, externalTermId)
            ?? throw new UserFriendlyException("You are not eligible to select courses for that term.");
        var policy = await GetCurrentPolicyAsync(externalTermId, profile, context);
        var offerings = await _lookup.GetAvailableOfferingsAsync(externalTermId);
        var sectionMappings = await GetSectionMappingsAsync();
        var registrations = (await _lookup.GetRegistrationsAsync(profile.ExternalStudentId, externalTermId))
            .Where(x => x.EffectiveWithdrawDate is null).ToArray();
        var courseAttempts = await GetCourseAttemptsAsync(profile);
        var reviewQuery = await _reviews.GetQueryableAsync();
        var reviews = await AsyncExecuter.ToListAsync(reviewQuery.Where(x =>
            x.StudentProfileId == profile.Id && x.ExternalTermId == externalTermId));
        var degreeRequirements = await GetOutstandingDegreeRequirementsAsync(profile);
        var graduationIntent = await _graduationIntents.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.ExternalTermId == externalTermId);

        return new CourseSelectionDto
        {
            IsLockedByBillApproval = locked,
            ExternalTermId = context.ExternalTermId,
            TermName = context.TermName,
            AttendanceType = context.AttendanceType,
            MaximumAllowedCredits = context.MaximumAllowedCredits,
            SelectedCredits = registrations.Sum(x => x.Credits),
            DegreeAuditAvailable = degreeRequirements is not null,
            IntendsToGraduate = graduationIntent?.IntendsToGraduate,
            Offerings = offerings.Select(x =>
            {
                var previousAttempt = FindPreviousAttempt(x, courseAttempts);
                return new CourseSelectionOfferingDto
                {
                ExternalOfferingId = x.ExternalOfferingId,
                CourseCode = CourseSelectionCourseCode.Format(x.Department, x.CourseCode, x.CourseType),
                Section = x.Section,
                CourseName = x.CourseName,
                Credits = x.Credits,
                CourseAttendanceType = string.Join(", ", GetCourseAttendanceTypes(x.Section, sectionMappings)),
                SeatsRemaining = x.SeatsRemaining,
                InstructorName = x.InstructorName,
                MeetingDays = x.MeetingDays,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                Meetings = CourseMeeting.Resolve(x.Meetings, x.MeetingDays, x.StartTime, x.EndTime),
                FulfillsDegreeRequirement = TryGetDegreeRequirement(x, degreeRequirements, out _),
                DegreeRequirementName = TryGetDegreeRequirement(x, degreeRequirements, out var requirementName)
                    ? requirementName
                    : null,
                WasPreviouslyTaken = previousAttempt is not null,
                PreviousGrade = GetPreviousGrade(previousAttempt),
                PreviousAttemptOutcome = GetPreviousAttemptOutcome(previousAttempt),
                CanSelect = !locked && CanSelect(policy, context, x, sectionMappings) &&
                    registrations.All(r => r.ExternalOfferingId != x.ExternalOfferingId)
                };
            }).ToList(),
            Registrations = registrations.Select(x => new CourseSelectionRegistrationDto
            {
                ExternalRegistrationId = x.ExternalRegistrationId,
                ExternalOfferingId = x.ExternalOfferingId,
                CourseCode = CourseSelectionCourseCode.Format(x.Department, x.CourseCode, x.CourseType),
                Section = x.Section,
                CourseName = x.CourseName,
                Credits = x.Credits,
                Status = x.RegistrationStatus,
                IsApprovedByScheduler = reviews.Any(r => r.ExternalCourseRegistrationId == x.ExternalRegistrationId && r.Status == CourseReviewStatus.Approved),
                CanRemove = !locked && string.Equals(x.RegistrationStatus, "Unofficial", StringComparison.OrdinalIgnoreCase) &&
                    reviews.Any(r => r.ExternalCourseRegistrationId == x.ExternalRegistrationId &&
                                     r.CanStudentRemove),
                NeedsReview = reviews.Any(r =>
                    r.ExternalCourseRegistrationId == x.ExternalRegistrationId && r.NeedsReview)
            }).ToList()
        };
    }

    private async Task<IReadOnlyDictionary<string, string>?> GetOutstandingDegreeRequirementsAsync(
        StudentProfile profile)
    {
        var lookup = _degreeAuditLookups.FirstOrDefault(x => x.Provider == profile.Provider);
        if (lookup is null)
            return null;

        try
        {
            var audit = (await lookup.GetAuditsAsync(profile.ExternalStudentId)).FirstOrDefault();
            if (audit is null)
                return null;

            var detail = await lookup.GetAuditAsync(profile.ExternalStudentId, audit.RevisionTermId,
                audit.AuditDegreeId, audit.AuditProgramId);
            if (detail is null)
                return null;

            return detail.Courses
                .Where(IsOutstandingDegreeCourse)
                .Where(x => !string.IsNullOrWhiteSpace(x.CourseCode))
                .GroupBy(x => NormalizeCourseCode(x.CourseCode))
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(course => !string.IsNullOrWhiteSpace(course.GroupName)
                            ? course.GroupName
                            : course.RequirementName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Degree requirement");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Degree audit requirements could not be loaded for course selection student {ExternalStudentId}.",
                profile.ExternalStudentId);
            return null;
        }
    }

    private static bool IsOutstandingDegreeCourse(StudentDegreeAuditCourse course)
    {
        var courseIsRemaining = string.Equals(course.CourseStatus, "R", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(course.CourseStatus, "MR", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(course.CourseStatus, "TR", StringComparison.OrdinalIgnoreCase);
        var groupIsRemaining = course.GroupCreditsCompleted < course.GroupCreditsRequired &&
                               !string.Equals(course.GroupStatus, "C", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(course.GroupStatus, "MC", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(course.GroupStatus, "TC", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(course.GroupStatus, "NN", StringComparison.OrdinalIgnoreCase);
        var requirementIsRemaining = course.RequirementCreditsCompleted < course.RequirementCreditsRequired &&
                                     !string.Equals(course.RequirementStatus, "C", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(course.RequirementStatus, "MC", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(course.RequirementStatus, "TC", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(course.RequirementStatus, "NN", StringComparison.OrdinalIgnoreCase);
        return courseIsRemaining && groupIsRemaining && requirementIsRemaining;
    }

    private static bool TryGetDegreeRequirement(
        CourseSelectionOffering offering,
        IReadOnlyDictionary<string, string>? requirements,
        out string? requirementName)
    {
        requirementName = null;
        if (requirements is null)
            return false;

        var fullCourseCode = NormalizeCourseCode($"{offering.Department}{offering.CourseCode}{offering.CourseType}");
        return requirements.TryGetValue(fullCourseCode, out requirementName);
    }

    private static string NormalizeCourseCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private async Task<IReadOnlyList<StudentCourseAttempt>> GetCourseAttemptsAsync(StudentProfile profile)
    {
        try
        {
            return await _lookup.GetCourseAttemptsAsync(profile.ExternalStudentId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Prior course attempts could not be loaded for course selection student {ExternalStudentId}.",
                profile.ExternalStudentId);
            return [];
        }
    }

    private static StudentCourseAttempt? FindPreviousAttempt(
        CourseSelectionOffering offering,
        IReadOnlyList<StudentCourseAttempt> attempts) =>
        attempts.FirstOrDefault(x =>
            string.Equals(x.Department.Trim(), offering.Department.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CourseCode.Trim(), offering.CourseCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CourseType.Trim(), offering.CourseType.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? GetPreviousGrade(StudentCourseAttempt? attempt)
    {
        if (attempt is null)
            return null;
        if (!string.IsNullOrWhiteSpace(attempt.Grade))
            return attempt.Grade.Trim();
        return attempt.WasWithdrawn ? "W" : null;
    }

    private static string GetPreviousAttemptOutcome(StudentCourseAttempt? attempt)
    {
        if (attempt is null)
            return "none";
        if (attempt.WasWithdrawn)
            return "unsuccessful";

        var grade = attempt.Grade.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(grade))
            return "pending";
        if (grade is "F" or "WF" or "W" or "WP" or "WD" or "AW" or "U" or "NC" or "NP")
            return "unsuccessful";
        if (grade is "I" or "IP" or "INP")
            return "pending";
        return "successful";
    }

    public async Task<bool> HasStudentStatusHoldAsync()
    {
        var profile = await GetCurrentProfileAsync();
        var contexts = await _lookup.GetEligibleContextsAsync(profile.ExternalStudentId);
        return contexts.Count > 0 && contexts.All(x => !x.IsMatriculated);
    }

    public async Task<List<CourseSelectionTermDto>> GetEligibleTermsAsync()
    {
        var profile = await GetCurrentProfileAsync();
        var configurations = await _termConfigurations.GetListAsync();
        var now = Clock.Now;
        var contexts = await _lookup.GetEligibleContextsAsync(profile.ExternalStudentId);
        contexts = contexts.Where(x => x.IsMatriculated && configurations.Any(c => c.ExternalTermId == x.ExternalTermId &&
            c.IsOpenForStudent(now, profile.StudentId, x.CollegeLevel, x.AttendanceType))).ToArray();
        return contexts
            .Select(x => new CourseSelectionTermDto
            {
                ExternalTermId = x.ExternalTermId,
                TermName = x.TermName
            }).ToList();
    }

    public async Task SaveGraduationIntentAsync(SaveGraduationIntentInput input)
    {
        if (_currentStudentView.IsImpersonating)
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Student impersonation is read-only.");
        if (string.IsNullOrWhiteSpace(input.ExternalTermId) || input.IntendsToGraduate is null)
            throw new UserFriendlyException("Please choose Yes or No before saving.");

        var profile = await GetCurrentProfileAsync();
        await EnsureChangesAllowedAsync(profile, input.ExternalTermId);
        var context = await _lookup.GetContextAsync(profile.ExternalStudentId, input.ExternalTermId);
        if (context is null)
            throw new UserFriendlyException("You are not eligible to select courses for that term.");
        await GetCurrentPolicyAsync(input.ExternalTermId, profile, context);

        var intent = await _graduationIntents.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.ExternalTermId == input.ExternalTermId);
        if (intent is null)
        {
            intent = new StudentGraduationIntent(GuidGenerator.Create(), CurrentTenant.Id,
                profile.Id, input.ExternalTermId, input.IntendsToGraduate.Value);
            await _graduationIntents.InsertAsync(intent, autoSave: true);
        }
        else
        {
            intent.Update(input.IntendsToGraduate.Value);
            await _graduationIntents.UpdateAsync(intent, autoSave: true);
        }
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<AddCourseSelectionResultDto> AddAsync(AddCourseSelectionInput input)
    {
        if (_currentStudentView.IsImpersonating)
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Student impersonation is read-only.");
        if (string.IsNullOrWhiteSpace(input.ExternalTermId) ||
            string.IsNullOrWhiteSpace(input.ExternalOfferingId) ||
            !Guid.TryParse(input.IdempotencyKey, out _))
            throw new UserFriendlyException("The course selection request is invalid.");

        StudentProfile profile;
        CourseSelectionPolicy policy;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            profile = await GetCurrentProfileAsync();
            await EnsureChangesAllowedAsync(profile, input.ExternalTermId);
            policy = await GetCurrentPolicyAsync(input.ExternalTermId, profile);
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

        var assignment = await GetAdvisorAssignmentAsync(context, profile);
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
        if (_currentStudentView.IsImpersonating)
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Student impersonation is read-only.");
        if (string.IsNullOrWhiteSpace(input.ExternalTermId) ||
            string.IsNullOrWhiteSpace(input.ExternalOfferingId) ||
            string.IsNullOrWhiteSpace(input.ExternalRegistrationId) ||
            !Guid.TryParse(input.IdempotencyKey, out _))
            throw new UserFriendlyException("The course removal request is invalid.");

        StudentProfile profile;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            profile = await GetCurrentProfileAsync();
            await EnsureChangesAllowedAsync(profile, input.ExternalTermId);
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
                x.ExternalTermId == input.ExternalTermId &&
                x.ExternalCourseOfferingId == input.ExternalOfferingId &&
                x.ExternalCourseRegistrationId == input.ExternalRegistrationId);
            if (review is null || !review.CanStudentRemove)
                throw new UserFriendlyException("Only courses awaiting advisor review can be removed through Course Selection.");
            review.BeginStudentRemoval(Clock.Now);
            await _reviews.UpdateAsync(review, autoSave: true);
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
        return await _currentStudentView.GetProfileAsync()
            ?? throw new UserFriendlyException("Your student profile could not be found.");
    }

    private async Task EnsureChangesAllowedAsync(StudentProfile profile, string externalTermId)
    {
        if (await _completion.IsApprovedAsync(profile, externalTermId))
            throw new UserFriendlyException("Your bill is approved for this term. Contact Student Billing to request course changes.");
    }

    private async Task<CourseSelectionPolicy> GetCurrentPolicyAsync(string externalTermId, StudentProfile profile,
        CourseSelectionContext? context = null)
    {
        var now = Clock.Now;
        context ??= await _lookup.GetContextAsync(profile.ExternalStudentId, externalTermId);
        if (context is null)
            throw new UserFriendlyException("You are not eligible to select courses for that term.");
        if (!context.IsMatriculated)
            throw new UserFriendlyException(RegistrationMessages.StudentStatusHold, code: RegistrationMessages.StudentStatusHoldCode);
        var termConfiguration = await _termConfigurations.FindAsync(x => x.ExternalTermId == externalTermId);
        if (termConfiguration is not null)
        {
            if (!termConfiguration.IsOpenForStudent(now, profile.StudentId,
                context.CollegeLevel, context.AttendanceType))
                throw new UserFriendlyException("Registration is not currently open for that term.");
            return termConfiguration.CreatePolicy(allowOutsideWindow: !termConfiguration.IsOpen(now));
        }
        throw new UserFriendlyException("Registration is not configured for that term.");
    }

    private async Task<AdvisorAssignment?> GetAdvisorAssignmentAsync(CourseSelectionContext context, StudentProfile profile)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var now = Clock.Now;
        var candidates = (await _advisorAssignments.GetListAsync(x => x.IsActive && x.EffectiveFrom <= now &&
            (x.EffectiveTo == null || x.EffectiveTo > now)))
            .Where(x => string.Equals(x.AttendanceType, context.AttendanceType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.AttendanceType, AdvisorAssignment.GlobalAttendanceType, StringComparison.OrdinalIgnoreCase)).ToArray();
        var graduating = await _graduationIntents.FindAsync(x => x.StudentProfileId == profile.Id && x.ExternalTermId == context.ExternalTermId);
        StudentPrimaryProgram? primary = null;
        if (candidates.Any(x => x.GetPrimaryProgramScope().IsRestricted))
        {
            var lookup = _programLookups.SingleOrDefault(x => x.Provider == profile.Provider);
            if (lookup is not null)
            {
                try { primary = (await lookup.GetAsync([new(profile.ExternalStudentId, context.ExternalTermId)])).SingleOrDefault(); }
                catch (Exception exception) { _logger.LogWarning(exception, "Cannot resolve primary program for scheduler routing; restricted assignments are excluded."); }
            }
        }
        var assignment = candidates.Where(x => x.IncludesStudent(context.AttendanceType, graduating?.IntendsToGraduate == true, primary))
            .OrderBy(x => x.AttendanceType == AdvisorAssignment.GlobalAttendanceType ? 1 : 0).ThenByDescending(x => x.EffectiveFrom).FirstOrDefault();
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

    private bool CanSelect(CourseSelectionPolicy policy, CourseSelectionContext context,
        CourseSelectionOffering offering, IReadOnlyList<CourseSectionAttendanceTypeMapping> mappings)
    {
        var attendanceTypes = GetCourseAttendanceTypes(offering.Section, mappings);
        return attendanceTypes.Count > 0 && policy.CanSelect(context, offering, attendanceTypes, Clock.Now);
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
