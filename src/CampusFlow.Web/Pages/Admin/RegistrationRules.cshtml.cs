using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.RegistrationRules)]
public class RegistrationRulesModel : CampusFlowPageModel
{
    private readonly IRepository<RegistrationTermConfiguration, Guid> _configurations;
    private readonly IRepository<CourseSectionAttendanceTypeMapping, Guid> _sectionMappings;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public RegistrationRulesModel(IRepository<RegistrationTermConfiguration, Guid> configurations,
        IRepository<CourseSectionAttendanceTypeMapping, Guid> sectionMappings,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups, IGuidGenerator guidGenerator,
        ITenantThemeProvider tenantThemeProvider)
    {
        _configurations = configurations;
        _sectionMappings = sectionMappings;
        _termLookups = termLookups.ToArray();
        _guidGenerator = guidGenerator;
        _tenantThemeProvider = tenantThemeProvider;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<RegistrationTermConfiguration> Configurations { get; private set; } = [];
    public IReadOnlyList<SelectListItem> Terms { get; private set; } = [];
    public IReadOnlyList<string> AttendanceTypes { get; private set; } = [];
    public TenantTheme Theme { get; private set; } = null!;

    public async Task OnGetAsync(Guid? edit = null, Guid? copyFrom = null)
    {
        await LoadListsAsync();
        var sourceId = edit ?? copyFrom;
        if (!sourceId.HasValue) return;
        var source = await _configurations.GetAsync(sourceId.Value);
        Input = Map(source);
        if (copyFrom.HasValue)
        {
            Input.Id = null;
            Input.ExternalTermId = string.Empty;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadListsAsync();
        var term = Terms.FirstOrDefault(x => x.Value == Input.ExternalTermId);
        if (term is null) ModelState.AddModelError("Input.ExternalTermId", "Choose a term from Elements.");
        if (Input.RegistrationClosesAt <= Input.RegistrationOpensAt)
            ModelState.AddModelError("Input.RegistrationClosesAt", "Closing must be after opening.");
        Input.Mappings = Input.Mappings.Where(x => !string.IsNullOrWhiteSpace(x.StudentAttendanceType) ||
            !string.IsNullOrWhiteSpace(x.CourseAttendanceType)).ToList();
        if (Input.Mappings.Any(x => string.IsNullOrWhiteSpace(x.StudentAttendanceType) ||
                                    string.IsNullOrWhiteSpace(x.CourseAttendanceType)))
            ModelState.AddModelError("Input.Mappings", "Complete both attendance types on every mapping row.");
        var duplicate = Configurations.FirstOrDefault(x => x.ExternalTermId == Input.ExternalTermId && x.Id != Input.Id);
        if (duplicate is not null) ModelState.AddModelError("Input.ExternalTermId", "That term already has a configuration. Edit it instead.");
        if (!ModelState.IsValid) return Page();

        var selected = (await _termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements)
            .GetTermsAsync(HttpContext.RequestAborted)).Single(x => x.ExternalTermId == Input.ExternalTermId);
        var mappingsJson = JsonSerializer.Serialize(Input.Mappings.Select(x =>
            new CourseSelectionAttendanceTypeMapping("*", x.StudentAttendanceType.Trim(), x.CourseAttendanceType.Trim())));
        if (Input.Id.HasValue)
        {
            var existing = await _configurations.GetAsync(Input.Id.Value);
            existing.Update(Input.RegistrationOpensAt, Input.RegistrationClosesAt, Input.IsEnabled,
                Input.RequireAdvisorReview, Input.EnforceSectionCapacity, mappingsJson);
            await _configurations.UpdateAsync(existing, autoSave: true);
            Alerts.Success($"{existing.TermName} registration was updated.");
        }
        else
        {
            var configuration = new RegistrationTermConfiguration(_guidGenerator.Create(),
                CurrentTenant.Id, selected.ExternalTermId, selected.TermCode, selected.DisplayName,
                Input.RegistrationOpensAt, Input.RegistrationClosesAt, Input.IsEnabled,
                Input.RequireAdvisorReview, Input.EnforceSectionCapacity, mappingsJson);
            await _configurations.InsertAsync(configuration, autoSave: true);
            Alerts.Success($"{selected.DisplayName} registration was configured.");
        }
        return RedirectToPage();
    }

    private async Task LoadListsAsync()
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        Configurations = (await _configurations.GetListAsync()).OrderByDescending(x => x.TermCode).ToArray();
        var terms = await _termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements)
            .GetTermsAsync(HttpContext.RequestAborted);
        Terms = terms.Select(x => new SelectListItem(x.DisplayName, x.ExternalTermId)).ToArray();
        AttendanceTypes = (await _sectionMappings.GetListAsync()).Select(x => x.AttendanceType)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
    }

    private static InputModel Map(RegistrationTermConfiguration source)
    {
        var mappings = JsonSerializer.Deserialize<CourseSelectionAttendanceTypeMapping[]>(source.AttendanceTypeMappingsJson) ?? [];
        return new InputModel { Id = source.Id, ExternalTermId = source.ExternalTermId,
            RegistrationOpensAt = source.RegistrationOpensAt, RegistrationClosesAt = source.RegistrationClosesAt,
            IsEnabled = source.IsEnabled, RequireAdvisorReview = source.RequireAdvisorReview,
            EnforceSectionCapacity = source.EnforceSectionCapacity,
            Mappings = mappings.Select(x => new MappingInput { StudentAttendanceType = x.StudentAttendanceType,
                CourseAttendanceType = x.CourseAttendanceType }).ToList() };
    }

    public class InputModel
    {
        public Guid? Id { get; set; }
        [Required] public string ExternalTermId { get; set; } = string.Empty;
        public DateTime RegistrationOpensAt { get; set; } = DateTime.Today;
        public DateTime RegistrationClosesAt { get; set; } = DateTime.Today.AddMonths(3);
        public bool IsEnabled { get; set; } = true;
        public bool RequireAdvisorReview { get; set; } = true;
        public bool EnforceSectionCapacity { get; set; } = true;
        public List<MappingInput> Mappings { get; set; } = [];
    }
    public class MappingInput
    {
        public string StudentAttendanceType { get; set; } = string.Empty;
        public string CourseAttendanceType { get; set; } = string.Empty;
    }
}
