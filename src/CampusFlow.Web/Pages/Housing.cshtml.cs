using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.BillApprovals;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace CampusFlow.Web.Pages;

[Authorize]
public class HousingModel : CampusFlowPageModel
{
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IMealPlanAppService _mealPlans;
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _billApprovalTermConfigurations;
    private readonly IClock _clock;
    private readonly ILogger<HousingModel> _logger;
    private readonly IElementsHousingReader _housingReader;
    private readonly ICurrentStudentView _currentStudentView;

    public HousingModel(ITenantThemeProvider themeProvider, IMealPlanAppService mealPlans,
        IRepository<BillApprovalTermConfiguration, Guid> billApprovalTermConfigurations,
        IClock clock, ILogger<HousingModel> logger, IElementsHousingReader housingReader,
        ICurrentStudentView currentStudentView)
    {
        _themeProvider = themeProvider;
        _mealPlans = mealPlans;
        _billApprovalTermConfigurations = billApprovalTermConfigurations;
        _clock = clock;
        _logger = logger;
        _housingReader = housingReader;
        _currentStudentView = currentStudentView;
    }

    [BindProperty(SupportsGet = true)] public string? Term { get; set; }
    [BindProperty] public SaveMealPlanSelectionInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public bool Saved { get; set; }
    public MealPlanSelectionDto? Selection { get; private set; }
    public StudentHousingAssignment? CurrentAssignment { get; private set; }
    public bool IsUnavailable { get; private set; }
    public TenantTheme Theme { get; private set; } = new("CampusFlow", "#274690", "#172554", "#667eea",
        "#A1A8AE", "#F7F8FA", "#172033", null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);

    public async Task OnGetAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        try
        {
            Selection = await _mealPlans.GetAsync(Term);
            if (!Selection.HousingRequired) { IsUnavailable = true; return; }
            Input.ExternalTermId = Selection.ExternalTermId;
            var student = await _currentStudentView.GetProfileAsync();
            if (student?.Provider == StudentInformationSystemProvider.ThesisElements &&
                int.TryParse(student.ExternalStudentId, out var studentUid) && int.TryParse(Selection.ExternalTermId, out var termId))
                CurrentAssignment = await _housingReader.GetStudentAssignmentAsync(studentUid, termCalendarId: termId);
            if (!Selection.IsSelectionOpen || !Selection.MealPlanEligible || !Selection.HasHousingAssignment)
            {
                IsUnavailable = true;
                return;
            }
            if (!Selection.Options.Values.Any(x => x.Count > 0))
            {
                IsUnavailable = true;
                return;
            }
            Input.HousingChoice = Selection.AssignedHousingChoice;
            Input.ExternalMealPlanId = Selection.SelectedMealPlanId;
        }
        catch (Exception exception)
        {
            IsUnavailable = true;
            _logger.LogWarning(exception, "Unable to load housing and meal plan selection.");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await IsEnabledAsync())
            return RedirectToPage("/Index");

        try
        {
            var result = await _mealPlans.SaveAsync(Input);
            return RedirectToPage(new { saved = true, term = Input.ExternalTermId });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to save housing and meal plan selection.");
            ModelState.AddModelError(string.Empty, exception.Message);
            await OnGetAsync();
            return Page();
        }
    }

    public static string ChoiceName(HousingChoice choice) => choice switch
    {
        HousingChoice.OnCampus => "On-campus residence hall",
        HousingChoice.SeniorHousing => "Regents senior housing",
        HousingChoice.Commuter => "Approved commuter",
        _ => choice.ToString()
    };

    public static string ChoiceDescription(HousingChoice choice) => choice switch
    {
        HousingChoice.OnCampus => "I will live in a university residence hall.",
        HousingChoice.SeniorHousing => "I am eligible for Regents senior housing.",
        HousingChoice.Commuter => "I have approval to live off campus.",
        _ => string.Empty
    };

    private async Task<bool> IsEnabledAsync() =>
        (await _billApprovalTermConfigurations.GetListAsync()).Any(x => x.IsOpen(_clock.Now));
}
