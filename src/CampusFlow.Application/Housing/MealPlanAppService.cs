using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.BillApprovals;
using Volo.Abp.Timing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Housing;

[Authorize]
public class MealPlanAppService : CampusFlowAppService, IMealPlanAppService
{
    private readonly IRepository<StudentProfile, Guid> _profiles;
    private readonly ICurrentStudentView _currentStudentView;
    private readonly IRepository<MealPlanConfiguration, Guid> _configurations;
    private readonly IRepository<StudentHousingSelection, Guid> _selections;
    private readonly IReadOnlyCollection<IStudentInformationSystemMealPlanService> _services;
    private readonly ILogger<MealPlanAppService> _logger;
    private readonly IElementsHousingReader _housing;
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _terms;
    private readonly IClock _clock;
    private readonly IBillApprovalCompletionLookup _completion;
    private readonly IStudentInformationSystemMealPlanRemovalService _assignments;
    private readonly IMealPlanRemovalCoordinator _removals;

    public MealPlanAppService(IRepository<StudentProfile, Guid> profiles,
        ICurrentStudentView currentStudentView,
        IRepository<MealPlanConfiguration, Guid> configurations,
        IRepository<StudentHousingSelection, Guid> selections,
        IEnumerable<IStudentInformationSystemMealPlanService> services,
        ILogger<MealPlanAppService> logger, IElementsHousingReader housing,
        IRepository<BillApprovalTermConfiguration, Guid> terms, IClock clock, IBillApprovalCompletionLookup completion,
        IStudentInformationSystemMealPlanRemovalService assignments, IMealPlanRemovalCoordinator removals)
    {
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _configurations = configurations;
        _selections = selections;
        _services = services.ToArray();
        _logger = logger;
        _housing = housing;
        _terms = terms;
        _clock = clock;
        _completion = completion;
        _assignments = assignments;
        _removals = removals;
    }

    public async Task<MealPlanSelectionDto> GetAsync(string? externalTermId = null)
    {
        var profile = await GetProfileAsync();
        var service = _services.SingleOrDefault(x => x.Provider == profile.Provider)
            ?? throw new UserFriendlyException("Meal plan selection is not available for your student record.");
        var terms = await _terms.GetListAsync();
        var term = string.IsNullOrWhiteSpace(externalTermId)
            ? terms.OrderByDescending(x => x.IsOpenForStudent(_clock.Now, profile.StudentId)).ThenBy(x => Math.Abs((x.OpensAt - _clock.Now).TotalDays)).FirstOrDefault()
            : terms.SingleOrDefault(x => x.ExternalTermId == externalTermId);
        if (term is null || !int.TryParse(term.ExternalTermId, out var termId) ||
            !int.TryParse(profile.ExternalStudentId, out var studentUid))
            throw new UserFriendlyException("A meal plan selection period is not available.");
        var eligibility = await _housing.GetStudentEligibilityAsync(studentUid, termId);
        var context = await service.GetContextAsync(profile.ExternalStudentId, termId);
        var selectionTerm = context.Terms.SingleOrDefault(x => x.TermCalendarId == termId)
            ?? throw new UserFriendlyException("The meal plan term is unavailable.");
        var configs = await _configurations.GetListAsync(x => x.IsActive);

        var result = new MealPlanSelectionDto
        {
            ExternalTermId = term.ExternalTermId,
            HousingRequired = eligibility.HousingRequired,
            HasHousingAssignment = eligibility.HasAssignment,
            MealPlanEligible = eligibility.MealPlanEligible,
            AssignedHousingChoice = eligibility.AssignedChoice,
            IsSelectionOpen = term.IsOpenForStudent(_clock.Now, profile.StudentId),
            IsLockedByBillApproval = await _completion.IsApprovedAsync(profile, term.ExternalTermId),
            StudentName = profile.DisplayName,
            StudentId = profile.StudentId,
            AttendanceType = context.AttendanceType,
        };

        result.TermName = selectionTerm.StartDate.ToString("MMMM yyyy");
        result.TermStartDate = selectionTerm.StartDate;
        result.TermEndDate = selectionTerm.EndDate;

        var selection = string.IsNullOrWhiteSpace(result.TermName)
            ? null
            : await _selections.FindAsync(x => x.StudentProfileId == profile.Id && x.TermName == result.TermName);
        if (selection is { ExternalMealPlanId: not null, SyncedToStudentInformationSystem: true } &&
            profile.Provider == StudentInformationSystemProvider.ThesisElements)
        {
            var assigned = (await _assignments.GetAssignmentsAsync(profile.ExternalStudentId))
                .Where(x => x.TermCalendarId == termId).ToArray();
            if (assigned.Length == 0) selection = null; // Read-only reconciliation; retain the prior choice for audit.
            else if (!assigned.Any(x => x.MealPlanId == selection.ExternalMealPlanId))
                throw new UserFriendlyException("Your meal-plan assignment has changed. Contact Student Billing to review it before choosing another plan.");
        }
        result.SelectedHousingChoice = selection?.HousingChoice;
        result.SelectedMealPlanId = selection?.ExternalMealPlanId;
        result.SelectedMealPlanName = selection?.MealPlanName;
        result.SyncedToStudentInformationSystem = selection?.SyncedToStudentInformationSystem;

        foreach (var choice in Enum.GetValues<HousingChoice>())
        {
            result.Options[choice] = configs
                .Where(x => eligibility.MealPlanEligible && eligibility.HasAssignment && choice == eligibility.AssignedChoice
                    && AllowsChoice(x, choice) && AllowsAttendanceType(x, context.AttendanceType))
                .OrderBy(x => x.SortOrder)
                .Select(x =>
                {
                    var external = x.IsNoPlanOption ? null : MealPlanCatalogRules.Resolve(x, context.Catalog, selectionTerm);
                    if (!x.IsNoPlanOption && external is null) return null;
                    return new MealPlanOptionDto
                    {
                        ExternalMealPlanId = x.IsNoPlanOption ? null : external!.MealPlanId,
                        Name = x.DisplayName,
                        Description = x.Description,
                        Price = x.DisplayPrice ?? external?.Amount,
                        IsNoPlanOption = x.IsNoPlanOption
                    };
                })
                .Where(x => x is not null)
                .Cast<MealPlanOptionDto>()
                .ToList();
        }
        if ((await _removals.PendingAsync(studentUid,termId)).Count > 0)
        {
            result.HasPendingRemoval = true; result.IsSelectionOpen = false; result.SelectedHousingChoice = null;
            result.UnavailableReason = "A meal-plan removal needs Business Services review before you can select another plan or approve your bill.";
            return result;
        }
        result.UnavailableReason = result.IsLockedByBillApproval
            ? "Your bill is approved for this term. Contact BusinessServices@nelson.edu to request changes."
            : !result.MealPlanEligible
            ? (eligibility.IsAmericanIndianCollege ? "Your housing includes your meal plan; a separate selection is not required."
                : "A separate meal plan is not available for your student record.")
            : !result.HasHousingAssignment
            ? "A housing assignment is required before choosing a meal plan. Contact Residence Life."
            : !result.IsSelectionOpen
            ? (term.Availability(_clock.Now, profile.StudentId) == "closed"
                ? "The meal-plan selection window for this term has closed. Contact Student Billing."
                : "Meal plan selection opens with bill approval for this term. Contact Student Billing if you need access.")
            : !result.Options.Values.Any(x => x.Count > 0)
            ? "Meal plans are not yet available for your assigned housing in this term. Contact Student Billing."
            : null;
        return result;
    }

    public async Task<MealPlanSelectionDto> SaveAsync(SaveMealPlanSelectionInput input)
    {
        if (_currentStudentView.IsImpersonating)
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Student impersonation is read-only.");
        if (input.ExternalMealPlanId is <= 0 ||
            input.ExternalMealPlanId.HasValue == input.SelectNoMealPlan)
            throw new UserFriendlyException("Choose a meal plan, or explicitly select No Meal Plan.");
        if (!Enum.IsDefined(input.HousingChoice)) throw new UserFriendlyException("Choose a housing option.");
        var profile = await GetProfileAsync();
        var view = await GetAsync(input.ExternalTermId);
        if (!int.TryParse(profile.ExternalStudentId,out var student) || !int.TryParse(view.ExternalTermId,out var termId)) throw new UserFriendlyException("Review the selected student and term.");
        await using var lease=await _removals.LockAsync(student,termId);
        view = await GetAsync(view.ExternalTermId);
        if (view.IsLockedByBillApproval)
            throw new UserFriendlyException("Your bill is approved for this term. Contact BusinessServices@nelson.edu to request changes.");
        if (!view.IsSelectionOpen || !view.MealPlanEligible || !view.HasHousingAssignment)
            throw new UserFriendlyException(view.UnavailableReason ?? "Meal plan selection is not available for your student record and term.");
        if (input.HousingChoice != view.AssignedHousingChoice)
            throw new UserFriendlyException("Your meal plan options must match your assigned housing.");
        var option = view.Options[input.HousingChoice].SingleOrDefault(x => x.ExternalMealPlanId == input.ExternalMealPlanId);
        if (option is null || option.IsNoPlanOption != input.SelectNoMealPlan)
            throw new UserFriendlyException("Choose an available meal plan.");

        if (string.IsNullOrWhiteSpace(view.TermName))
            throw new UserFriendlyException("A housing selection period is not available.");
        var selection = await _selections.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.TermName == view.TermName);
        if (profile.Provider == StudentInformationSystemProvider.ThesisElements)
        {
            var assigned = await _assignments.GetAssignmentsAsync(profile.ExternalStudentId);
            if (assigned.Any(x => x.TermCalendarId.ToString(System.Globalization.CultureInfo.InvariantCulture) == view.ExternalTermId))
                throw new UserFriendlyException("A meal plan is already assigned for this term. Contact Student Billing before choosing another plan.");
            if (selection is { ExternalMealPlanId: not null, SyncedToStudentInformationSystem: true })
            {
                // Replace only a choice whose removal has just been verified in live Elements.
                await _selections.DeleteAsync(selection, autoSave: true);
                selection = null;
            }
        }
        if (selection is not null)
            throw new UserFriendlyException("Your housing and meal plan selection has already been recorded.");

        if (selection is null)
        {
            selection = new StudentHousingSelection(GuidGenerator.Create(), CurrentTenant.Id, profile.Id,
                profile.ExternalStudentId, view.TermName, view.TermStartDate, view.TermEndDate,
                input.HousingChoice, option.ExternalMealPlanId, option.Name);
            await _selections.InsertAsync(selection, autoSave: true);
        }

        if (option.IsNoPlanOption)
        {
            selection.MarkSynced();
        }
        else
        {
            try
            {
                var service = _services.Single(x => x.Provider == profile.Provider);
                await service.AssignAsync(profile.ExternalStudentId, option.ExternalMealPlanId!.Value);
                selection.MarkSynced();
            }
            catch (Exception exception)
            {
                selection.MarkPending(exception.Message);
                _logger.LogWarning(exception, "Meal plan selection was stored in CampusFlow but could not be assigned in Elements for student {StudentId}.", profile.ExternalStudentId);
            }
        }
        await _selections.UpdateAsync(selection, autoSave: true);
        return await GetAsync(view.ExternalTermId);
    }

    private async Task<StudentProfile> GetProfileAsync()
    {
        return await _currentStudentView.GetProfileAsync()
            ?? throw new UserFriendlyException("A student record could not be found.");
    }

    private static bool AllowsChoice(MealPlanConfiguration configuration, HousingChoice choice)
    {
        try { return (JsonSerializer.Deserialize<HousingChoice[]>(configuration.HousingChoicesJson) ?? []).Contains(choice); }
        catch (JsonException) { return false; }
    }

    private static bool AllowsAttendanceType(MealPlanConfiguration configuration, string attendanceType)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(configuration.EligibleAttendanceTypesJson) ?? [];
            return values.Length == 0 || values.Contains(attendanceType, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }
}
