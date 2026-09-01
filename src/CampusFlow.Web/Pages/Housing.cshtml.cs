using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.BillApprovals;
using CampusFlow.Housing;
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

    public HousingModel(ITenantThemeProvider themeProvider, IMealPlanAppService mealPlans,
        IRepository<BillApprovalTermConfiguration, Guid> billApprovalTermConfigurations,
        IClock clock, ILogger<HousingModel> logger)
    {
        _themeProvider = themeProvider;
        _mealPlans = mealPlans;
        _billApprovalTermConfigurations = billApprovalTermConfigurations;
        _clock = clock;
        _logger = logger;
    }

    [BindProperty] public SaveMealPlanSelectionInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public bool Saved { get; set; }
    public MealPlanSelectionDto? Selection { get; private set; }
    public bool IsUnavailable { get; private set; }
    public TenantTheme Theme { get; private set; } = new("CampusFlow", "#274690", "#172554", "#667eea",
        "#A1A8AE", "#F7F8FA", "#172033", null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);

    public async Task OnGetAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        try
        {
            if (!await IsEnabledAsync())
            {
                IsUnavailable = true;
                return;
            }
            Selection = await _mealPlans.GetAsync();
            if (!Selection.Options.Values.Any(x => x.Count > 0))
            {
                IsUnavailable = true;
                return;
            }
            Input.HousingChoice = Selection.SelectedHousingChoice ?? HousingChoice.OnCampus;
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
            return RedirectToPage(new { saved = true });
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
