using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace CampusFlow.Web.Pages.Admin.BusinessServices;

[Authorize(CampusFlowPermissions.Admin.AddStudentMealPlan)]
public class AddStudentMealPlanModel : CampusFlowPageModel
{
    private readonly IAdminMealPlanAppService _service;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly ILogger<AddStudentMealPlanModel> _logger;

    public AddStudentMealPlanModel(IAdminMealPlanAppService service, ITenantThemeProvider themeProvider,
        ILogger<AddStudentMealPlanModel> logger)
    {
        _service = service;
        _themeProvider = themeProvider;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)] public string Query { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string? Student { get; set; }
    [BindProperty(SupportsGet = true)] public int? Term { get; set; }
    [BindProperty(SupportsGet = true)] public int? HousingStatus { get; set; }
    [BindProperty(SupportsGet = true)] public int? MealPlan { get; set; }
    [BindProperty] public AssignInputModel Input { get; set; } = new();

    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<AdminMealPlanStudentDto> Results { get; private set; } = [];
    public AdminMealPlanContextDto? Context { get; private set; }
    public AdminMealPlanOptionDto? Preview { get; private set; }
    public string? SearchError { get; private set; }

    public async Task OnGetAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        if (!string.IsNullOrWhiteSpace(Query) && Query.Trim().Length >= 2)
        {
            try { Results = await _service.SearchStudentsAsync(Query); }
            catch (SqlException exception) when (exception.Number == -2)
            {
                SearchError = "The student search took too long. Please try again.";
                _logger.LogWarning(exception, "Admin meal-plan student search timed out.");
            }
        }
        if (!string.IsNullOrWhiteSpace(Student))
        {
            Context = await _service.GetContextAsync(Student, Term);
            Term ??= Context.SelectedTermCalendarId;
            HousingStatus ??= Context.CurrentHousingStatusId;
            Preview = MealPlan.HasValue
                ? Context.Options.SingleOrDefault(x => x.MealPlanId == MealPlan.Value)
                : null;
        }
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        Context = await _service.GetContextAsync(Input.ExternalStudentId, Input.TermCalendarId);
        Preview = Context.Options.SingleOrDefault(x => x.MealPlanId == Input.MealPlanId);
        if (Preview is null)
            ModelState.AddModelError("Input.MealPlanId", "That meal plan is no longer available.");
        if (Context.HousingStatuses.All(x => x.Id != Input.HousingStatusId))
            ModelState.AddModelError("Input.HousingStatusId", "That housing status is no longer available.");
        if (!Input.Confirmed)
            ModelState.AddModelError("Input.Confirmed", "Confirm that you intend to assign this meal plan.");
        if (!ModelState.IsValid) return Page();

        await _service.AssignAsync(new AssignStudentMealPlanInput
        {
            ExternalStudentId = Input.ExternalStudentId,
            TermCalendarId = Input.TermCalendarId,
            HousingStatusId = Input.HousingStatusId,
            MealPlanId = Input.MealPlanId
        });
        var housing = Context.HousingStatuses.Single(x => x.Id == Input.HousingStatusId);
        Alerts.Success($"{housing.Name} and {Preview!.Name} were assigned to {Context.Student.Name} ({Context.Student.StudentId}).");
        return RedirectToPage(new { query = Context.Student.StudentId, student = Input.ExternalStudentId,
            term = Input.TermCalendarId });
    }

    public class AssignInputModel
    {
        [Required] public string ExternalStudentId { get; set; } = string.Empty;
        [Range(1, int.MaxValue)] public int TermCalendarId { get; set; }
        [Range(1, int.MaxValue)] public int HousingStatusId { get; set; }
        [Range(1, int.MaxValue)] public int MealPlanId { get; set; }
        public bool Confirmed { get; set; }
    }
}
