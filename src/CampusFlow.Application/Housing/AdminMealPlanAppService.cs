using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Housing;

[Authorize(CampusFlowPermissions.Admin.AddStudentMealPlan)]
public class AdminMealPlanAppService : CampusFlowAppService, IAdminMealPlanAppService
{
    private readonly IStudentInformationSystemStudentLookup _students;
    private readonly IStudentInformationSystemMealPlanService _mealPlans;
    private readonly ILogger<AdminMealPlanAppService> _logger;
    private readonly IStudentInformationSystemMealPlanRemovalService _removal;
    private readonly IRepository<StudentHousingSelection, Guid> _localSelections;

    public AdminMealPlanAppService(IEnumerable<IStudentInformationSystemStudentLookup> students,
        IEnumerable<IStudentInformationSystemMealPlanService> mealPlans, ILogger<AdminMealPlanAppService> logger,
        IStudentInformationSystemMealPlanRemovalService removal, IRepository<StudentHousingSelection, Guid> localSelections)
    {
        _students = students.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        _mealPlans = mealPlans.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        _logger = logger;_removal=removal;_localSelections=localSelections;
    }

    public async Task<List<AdminMealPlanStudentDto>> SearchStudentsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];
        var results = await _students.SearchAsync(query.Trim());
        return results.Select(MapStudent).ToList();
    }

    public async Task<AdminMealPlanContextDto> GetContextAsync(string externalStudentId, int? termCalendarId = null)
    {
        var student = await _students.FindByExternalStudentIdAsync(externalStudentId)
            ?? throw new UserFriendlyException("That student could not be found.");
        var context = await _mealPlans.GetContextAsync(externalStudentId, termCalendarId);
        var selectedTerm = context.Terms.SingleOrDefault(x => x.TermCalendarId == context.SelectedTermCalendarId)
            ?? throw new UserFriendlyException("Choose an available term before managing meal-plan assignments.");
        var localTermName = selectedTerm.StartDate.ToString("MMMM yyyy");
        var localNoneSelections = await _localSelections.GetListAsync(x =>
            x.ExternalStudentId == externalStudentId && x.TermName == localTermName &&
            x.TermStartDate == selectedTerm.StartDate && x.TermEndDate == selectedTerm.EndDate &&
            x.ExternalMealPlanId == null);
        return new AdminMealPlanContextDto
        {
            NoMealPlanSelections = localNoneSelections.Select(x =>
                new AdminNoMealPlanSelectionDto(x.Id, x.SubmittedAt, x.ConcurrencyStamp)).ToList(),
            CanRemove=_removal.IsEnabled,
            Assignments=(await _removal.GetAssignmentsAsync(externalStudentId)).Where(x=>x.TermCalendarId==context.SelectedTermCalendarId)
                .Select(x=>new AdminMealPlanAssignmentDto(x.AssignmentId,x.MealPlanId,x.TermCalendarId,x.Name,x.Amount)).ToList(),
            Student = MapStudent(student),
            AttendanceType = context.AttendanceType,
            SelectedTermCalendarId = context.SelectedTermCalendarId,
            CurrentHousingStatusId = context.CurrentHousingStatusId,
            Terms = context.Terms.Select(x => new AdminMealPlanTermDto
            {
                TermCalendarId = x.TermCalendarId,
                Name = x.Name
            }).ToList(),
            HousingStatuses = context.HousingStatuses.Select(x => new AdminHousingStatusDto
            {
                Id = x.Id,
                Name = x.Name
            }).ToList(),
            Options = context.Catalog
                .Where(x => x.StartDate <= selectedTerm.EndDate && x.EndDate >= selectedTerm.StartDate)
                .OrderByDescending(x => x.StartDate)
                .ThenBy(x => x.Name)
                .Select(x => new AdminMealPlanOptionDto
                {
                    MealPlanId = x.MealPlanId,
                    Name = x.Name,
                    Description = x.Description,
                    Amount = x.Amount,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate
                }).ToList()
        };
    }

    [Audited]
    public async Task AssignAsync(AssignStudentMealPlanInput input)
    {
        var context = await GetContextAsync(input.ExternalStudentId, input.TermCalendarId);
        if (context.SelectedTermCalendarId != input.TermCalendarId)
            throw new UserFriendlyException("That term is no longer available.");
        var housingStatus = context.HousingStatuses.SingleOrDefault(x => x.Id == input.HousingStatusId)
            ?? throw new UserFriendlyException("That housing status is no longer available.");
        var option = context.Options.SingleOrDefault(x => x.MealPlanId == input.MealPlanId)
            ?? throw new UserFriendlyException("That meal plan is no longer available.");
        if (context.Assignments.Any(x => x.MealPlanId == input.MealPlanId && x.TermCalendarId == input.TermCalendarId))
            throw new UserFriendlyException("This meal plan is already assigned for the selected term. Review the current assignment instead of adding it again.");
        try
        {
            await _mealPlans.UpdateHousingStatusAsync(input.ExternalStudentId, input.TermCalendarId,
                input.HousingStatusId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Elements housing-status update failed for student {StudentId}.",
                context.Student.StudentId);
            throw new UserFriendlyException("Elements could not update the housing status. No meal plan was assigned.");
        }
        try
        {
            await _mealPlans.AssignAsync(input.ExternalStudentId, input.MealPlanId);
            var verified = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var assignments = await _removal.GetAssignmentsAsync(input.ExternalStudentId);
                    verified = assignments.Any(x => x.MealPlanId == input.MealPlanId && x.TermCalendarId == input.TermCalendarId);
                    if (verified) break;
                }
                catch (InvalidOperationException) when (attempt < 2) { }
                if (attempt < 2) await Task.Delay(1000);
            }
            if (!verified) throw new InvalidOperationException("The requested meal assignment was not returned by Elements for the selected term.");
            _logger.LogWarning(
                "Housing status {HousingStatusId} ({HousingStatusName}) and meal plan {MealPlanId} ({MealPlanName}) were assigned to student {StudentId} for term {TermCalendarId} by administrator {UserId}.",
                housingStatus.Id, housingStatus.Name, option.MealPlanId, option.Name, context.Student.StudentId,
                input.TermCalendarId, CurrentUser.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Elements meal-plan assignment failed for student {StudentId}.",
                context.Student.StudentId);
            throw new UserFriendlyException($"The housing status was updated to {housingStatus.Name}, but the meal-plan assignment could not be confirmed. Refresh and check current assignments and Elements before trying again; the request may already have succeeded.");
        }
    }

    [Audited]
    [Authorize(CampusFlowPermissions.Admin.RemoveStudentMealPlan)]
    public async Task ClearNoMealPlanSelectionAsync(ClearNoMealPlanSelectionInput input)
    {
        if (CurrentTenant.Id is null || !input.Confirmed || input.SelectionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 ||
            string.IsNullOrWhiteSpace(input.ConcurrencyStamp))
            throw new UserFriendlyException("Confirm the student and term and provide a reason for clearing No Meal Plan.");
        var context = await GetContextAsync(input.ExternalStudentId, input.TermCalendarId);
        if (context.SelectedTermCalendarId != input.TermCalendarId ||
            !context.NoMealPlanSelections.Any(x => x.SelectionId == input.SelectionId && x.ConcurrencyStamp == input.ConcurrencyStamp))
            throw new UserFriendlyException("The recorded choice changed. Refresh before clearing it.");
        if (context.Assignments.Count > 0)
            throw new UserFriendlyException("This student already has an Elements meal-plan assignment for this term. Review that assignment before resetting the recorded choice.");
        var selection = await _localSelections.GetAsync(input.SelectionId);
        if (selection.TenantId != CurrentTenant.Id || selection.ExternalStudentId != input.ExternalStudentId ||
            selection.ExternalMealPlanId.HasValue || selection.ConcurrencyStamp != input.ConcurrencyStamp)
            throw new UserFriendlyException("The recorded choice changed. Refresh before clearing it.");
        // FullAuditedAggregateRoot uses soft deletion; the original choice and actor remain auditable.
        await _localSelections.DeleteAsync(selection, autoSave: true);
        _logger.LogInformation("No Meal Plan choice {SelectionId} cleared for term {TermId} by administrator {UserId}.",
            selection.Id, input.TermCalendarId, CurrentUser.Id);
    }

    [Audited]
    [Authorize(CampusFlowPermissions.Admin.RemoveStudentMealPlan)]
    public async Task RemoveAsync(RemoveStudentMealPlanInput input)
    {
        if(CurrentTenant.Id is null || !input.Confirmed)throw new UserFriendlyException("Select a university and confirm the reviewed meal-plan removal.");
        var context=await GetContextAsync(input.ExternalStudentId,input.TermCalendarId);
        if(!context.CanRemove || context.SelectedTermCalendarId!=input.TermCalendarId || !context.Terms.Any(x=>x.TermCalendarId==input.TermCalendarId) ||
            !context.Assignments.Any(x=>x.AssignmentId==input.AssignmentId&&x.MealPlanId==input.MealPlanId))
            throw new UserFriendlyException("The selected meal assignment changed. Refresh before removing it.");
        try {await _removal.RemoveAsync(input.ExternalStudentId,input.TermCalendarId,input.AssignmentId,input.MealPlanId);}
        catch(Exception e){_logger.LogError(e,"Meal-plan removal could not be confirmed for assignment {AssignmentId}.",input.AssignmentId);throw new UserFriendlyException("Meal-plan removal could not be confirmed. Refresh and check Elements before retrying.");}
        _logger.LogWarning("Meal assignment {AssignmentId} removed for term {TermId} by administrator {UserId}.",input.AssignmentId,input.TermCalendarId,CurrentUser.Id);
    }

    private static AdminMealPlanStudentDto MapStudent(StudentInformationSystemStudent student) => new()
    {
        ExternalStudentId = student.ExternalStudentId,
        StudentId = student.StudentId,
        Name = $"{(string.IsNullOrWhiteSpace(student.PreferredName) ? student.FirstName : student.PreferredName)} {student.LastName}".Trim(),
        Email = student.Email
    };
}
