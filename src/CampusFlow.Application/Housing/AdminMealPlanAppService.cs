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

namespace CampusFlow.Housing;

[Authorize(CampusFlowPermissions.Admin.AddStudentMealPlan)]
public class AdminMealPlanAppService : CampusFlowAppService, IAdminMealPlanAppService
{
    private readonly IStudentInformationSystemStudentLookup _students;
    private readonly IStudentInformationSystemMealPlanService _mealPlans;
    private readonly ILogger<AdminMealPlanAppService> _logger;

    public AdminMealPlanAppService(IEnumerable<IStudentInformationSystemStudentLookup> students,
        IEnumerable<IStudentInformationSystemMealPlanService> mealPlans, ILogger<AdminMealPlanAppService> logger)
    {
        _students = students.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        _mealPlans = mealPlans.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        _logger = logger;
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
        var selectedTerm = context.Terms.SingleOrDefault(x => x.TermCalendarId == context.SelectedTermCalendarId);
        return new AdminMealPlanContextDto
        {
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
                .Where(x => selectedTerm is null || (x.StartDate <= selectedTerm.EndDate && x.EndDate >= selectedTerm.StartDate))
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
            _logger.LogWarning(
                "Housing status {HousingStatusId} ({HousingStatusName}) and meal plan {MealPlanId} ({MealPlanName}) were assigned to student {StudentId} for term {TermCalendarId} by administrator {UserId}.",
                housingStatus.Id, housingStatus.Name, option.MealPlanId, option.Name, context.Student.StudentId,
                input.TermCalendarId, CurrentUser.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Elements meal-plan assignment failed for student {StudentId}.",
                context.Student.StudentId);
            throw new UserFriendlyException($"The housing status was updated to {housingStatus.Name}, but Elements could not assign the meal plan. Please retry the meal-plan assignment.");
        }
    }

    private static AdminMealPlanStudentDto MapStudent(StudentInformationSystemStudent student) => new()
    {
        ExternalStudentId = student.ExternalStudentId,
        StudentId = student.StudentId,
        Name = $"{(string.IsNullOrWhiteSpace(student.PreferredName) ? student.FirstName : student.PreferredName)} {student.LastName}".Trim(),
        Email = student.Email
    };
}
