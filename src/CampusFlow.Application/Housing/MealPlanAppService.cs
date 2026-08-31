using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
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

    public MealPlanAppService(IRepository<StudentProfile, Guid> profiles,
        ICurrentStudentView currentStudentView,
        IRepository<MealPlanConfiguration, Guid> configurations,
        IRepository<StudentHousingSelection, Guid> selections,
        IEnumerable<IStudentInformationSystemMealPlanService> services,
        ILogger<MealPlanAppService> logger)
    {
        _profiles = profiles;
        _currentStudentView = currentStudentView;
        _configurations = configurations;
        _selections = selections;
        _services = services.ToArray();
        _logger = logger;
    }

    public async Task<MealPlanSelectionDto> GetAsync()
    {
        var profile = await GetProfileAsync();
        var service = _services.SingleOrDefault(x => x.Provider == profile.Provider)
            ?? throw new UserFriendlyException("Meal plan selection is not available for your student record.");
        var context = await service.GetContextAsync(profile.ExternalStudentId);
        var configs = await _configurations.GetListAsync(x => x.IsActive);
        var catalogByName = context.Catalog
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.StartDate).First(), StringComparer.OrdinalIgnoreCase);

        var result = new MealPlanSelectionDto
        {
            StudentName = profile.DisplayName,
            StudentId = profile.StudentId,
            AttendanceType = context.AttendanceType,
        };

        var datedPlans = context.Catalog.Where(x => !x.Name.Equals("John", StringComparison.OrdinalIgnoreCase)).ToList();
        if (datedPlans.Count > 0)
        {
            var latestStart = datedPlans.Max(x => x.StartDate);
            var activeTerm = datedPlans.Where(x => x.StartDate == latestStart).ToList();
            result.TermName = latestStart.ToString("MMMM yyyy");
            result.TermStartDate = activeTerm.Min(x => x.StartDate);
            result.TermEndDate = activeTerm.Max(x => x.EndDate);
        }

        var selection = string.IsNullOrWhiteSpace(result.TermName)
            ? null
            : await _selections.FindAsync(x => x.StudentProfileId == profile.Id && x.TermName == result.TermName);
        result.SelectedHousingChoice = selection?.HousingChoice;
        result.SelectedMealPlanId = selection?.ExternalMealPlanId;
        result.SelectedMealPlanName = selection?.MealPlanName;
        result.SyncedToStudentInformationSystem = selection?.SyncedToStudentInformationSystem;

        foreach (var choice in Enum.GetValues<HousingChoice>())
        {
            result.Options[choice] = configs
                .Where(x => AllowsChoice(x, choice) && AllowsAttendanceType(x, context.AttendanceType))
                .OrderBy(x => x.SortOrder)
                .Select(x =>
                {
                    catalogByName.TryGetValue(x.ExternalMealPlanName, out var external);
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
        return result;
    }

    public async Task<MealPlanSelectionDto> SaveAsync(SaveMealPlanSelectionInput input)
    {
        if (_currentStudentView.IsImpersonating)
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Student impersonation is read-only.");
        if (!Enum.IsDefined(input.HousingChoice)) throw new UserFriendlyException("Choose a housing option.");
        var profile = await GetProfileAsync();
        var view = await GetAsync();
        var option = view.Options[input.HousingChoice].SingleOrDefault(x => x.ExternalMealPlanId == input.ExternalMealPlanId);
        if (option is null) throw new UserFriendlyException("Choose an available meal plan.");

        if (string.IsNullOrWhiteSpace(view.TermName))
            throw new UserFriendlyException("A housing selection period is not available.");
        var selection = await _selections.FindAsync(x =>
            x.StudentProfileId == profile.Id && x.TermName == view.TermName);
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
        return await GetAsync();
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
