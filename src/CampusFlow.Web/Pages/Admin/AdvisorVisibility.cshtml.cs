using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.RegistrationRules)]
public class AdvisorVisibilityModel : CampusFlowPageModel
{
    private readonly IRepository<AdvisorAssignment, Guid> _assignments;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _termConfigurations;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IGuidGenerator _guidGenerator;

    private readonly IReadOnlyCollection<IStudentPrimaryProgramLookup> _programLookups;
    public IReadOnlyList<StudentProgramChoice> ProgramChoices { get; private set; } = [];

    public AdvisorVisibilityModel(IRepository<AdvisorAssignment, Guid> assignments,
        IRepository<RegistrationTermConfiguration, Guid> termConfigurations,
        IStudentInformationSystemAdvisorLookup advisorLookup, ITenantThemeProvider themeProvider,
        IGuidGenerator guidGenerator, IEnumerable<IStudentPrimaryProgramLookup>? programLookups = null)
    {
        _assignments = assignments;
        _termConfigurations = termConfigurations;
        _advisorLookup = advisorLookup;
        _themeProvider = themeProvider;
        _guidGenerator = guidGenerator;
        _programLookups = programLookups?.ToArray() ?? [];
    }

    [BindProperty] public List<RoutingRow> Rows { get; set; } = [];
    public TenantTheme Theme { get; private set; } = null!;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<JsonResult> OnGetSearchAsync(string q)
    {
        var advisors = await _advisorLookup.SearchAsync(q, HttpContext.RequestAborted);
        return new JsonResult(advisors.Select(x => new
        {
            id = x.ExternalAdvisorId, email = x.Email, name = x.DisplayName
        }));
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadProgramChoicesAsync();
        var programOptions = new Dictionary<string, Dictionary<string, PrimaryProgramScope>>(StringComparer.OrdinalIgnoreCase);
        if (Rows.GroupBy(x => x.AttendanceType, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            ModelState.AddModelError(string.Empty, "Each attendance type can appear only once.");

        var selections = new Dictionary<string, List<AdvisorLookupResult>>(StringComparer.OrdinalIgnoreCase);
        var graduationOptions = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            try
            {
                var scopes = JsonSerializer.Deserialize<Dictionary<string, PrimaryProgramScope>>(row.ProgramScopesJson) ?? throw new JsonException();
                programOptions[row.AttendanceType] = new(scopes, StringComparer.OrdinalIgnoreCase);
                foreach (var scope in scopes.Values)
                {
                    var normalized = scope.Normalize();
                    if (normalized.MajorIds.Except(ProgramChoices.Select(x => x.Id)).Any() ||
                        normalized.DegreeIds.Except(ProgramChoices.Where(x => x.DegreeId.HasValue).Select(x => x.DegreeId!.Value)).Any() ||
                        normalized.ProgramIds.Except(ProgramChoices.Where(x => x.BlockProgramId.HasValue).Select(x => x.BlockProgramId!.Value)).Any())
                        throw new ArgumentException();
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                ModelState.AddModelError(string.Empty, "Choose valid primary program, major and degree restrictions. No assignments were saved.");
                programOptions[row.AttendanceType] = [];
            }
            var emails = ReadEmails(row.AdvisorEmailsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            graduationOptions[row.AttendanceType] = ReadGraduationOptions(row.GraduationOptionsJson);
            var resolved = new List<AdvisorLookupResult>();
            foreach (var email in emails)
            {
                var advisor = await _advisorLookup.FindAsync(email, HttpContext.RequestAborted);
                if (advisor is null)
                    ModelState.AddModelError(string.Empty,
                        $"The selected advisor '{email}' is no longer an active Elements user.");
                else resolved.Add(advisor);
            }
            selections[row.AttendanceType] = resolved;
        }
        if (!ModelState.IsValid)
        {
            foreach (var row in Rows)
            {
                row.Advisors = selections.GetValueOrDefault(row.AttendanceType, [])
                    .Select(x => new SelectedAdvisor(x.Email, x.DisplayName,
                        graduationOptions.GetValueOrDefault(row.AttendanceType, []).GetValueOrDefault(x.Email, true),
                        programOptions.GetValueOrDefault(row.AttendanceType, []).GetValueOrDefault(x.Email, PrimaryProgramScope.All)))
                    .ToArray();
                row.AdvisorEmailsJson = JsonSerializer.Serialize(row.Advisors.Select(x => x.Email));
                row.GraduationOptionsJson = JsonSerializer.Serialize(row.Advisors.ToDictionary(
                    x => x.Email, x => x.IncludeGraduatingStudents));
            }
            Theme = _themeProvider.Get(CurrentTenant.Name);
            return Page();
        }

        var now = Clock.Now;
        var active = await _assignments.GetListAsync(x => x.IsActive);
        foreach (var row in Rows)
        {
            var desired = selections[row.AttendanceType];
            var current = active.Where(x => string.Equals(x.AttendanceType, row.AttendanceType,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var desiredEmails = desired.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in current.Where(x => !desiredEmails.Contains(x.AdvisorEmail)))
            {
                assignment.Retire(now);
                await _assignments.UpdateAsync(assignment, autoSave: true);
            }
            var currentEmails = current.Select(x => x.AdvisorEmail).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in current.Where(x => desiredEmails.Contains(x.AdvisorEmail)))
            {
                var include = graduationOptions[row.AttendanceType].GetValueOrDefault(assignment.AdvisorEmail, true);

                assignment.SetIncludeGraduatingStudents(include);
                assignment.ConfigurePrimaryProgramScope(programOptions[row.AttendanceType].GetValueOrDefault(assignment.AdvisorEmail, PrimaryProgramScope.All));
                await _assignments.UpdateAsync(assignment, autoSave: true);
            }
            foreach (var advisor in desired.Where(x => !currentEmails.Contains(x.Email)))
            {
                var assignment = new AdvisorAssignment(_guidGenerator.Create(), CurrentTenant.Id,
                    row.AttendanceType, advisor.ExternalAdvisorId, advisor.Email, advisor.DisplayName, now,
                    includeGraduatingStudents: graduationOptions[row.AttendanceType].GetValueOrDefault(advisor.Email, true));
                assignment.ConfigurePrimaryProgramScope(programOptions[row.AttendanceType].GetValueOrDefault(advisor.Email, PrimaryProgramScope.All));
                await _assignments.InsertAsync(assignment, autoSave: true);
            }
        }
        Alerts.Success("Advisor assignments were saved.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        await LoadProgramChoicesAsync();
        var active = await _assignments.GetListAsync(x => x.IsActive);
        var types = (await _termConfigurations.GetListAsync())
            .SelectMany(x => ReadStudentAttendanceTypes(x.AttendanceTypeMappingsJson))
            .Concat(active.Select(x => x.AttendanceType))
            .Append(AdvisorAssignment.GlobalAttendanceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x == AdvisorAssignment.GlobalAttendanceType ? 0 : 1)
            .ThenBy(x => x).ToArray();
        Rows = types.Select(type =>
        {
            var advisors = active.Where(x => string.Equals(x.AttendanceType, type,
                StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.AdvisorDisplayName)
                .Select(x => new SelectedAdvisor(x.AdvisorEmail, x.AdvisorDisplayName,
                    x.IncludeGraduatingStudents, x.GetPrimaryProgramScope())).ToArray();
            return new RoutingRow { AttendanceType = type, AdvisorEmailsJson =
                JsonSerializer.Serialize(advisors.Select(x => x.Email)), GraduationOptionsJson =
                JsonSerializer.Serialize(advisors.ToDictionary(x => x.Email, x => x.IncludeGraduatingStudents)),
                ProgramScopesJson = JsonSerializer.Serialize(advisors.ToDictionary(x => x.Email, x => x.Scope)),
                Advisors = advisors };
        }).ToList();
    }

    private async Task LoadProgramChoicesAsync()
    {
        var lookup = _programLookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (lookup is null || !string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)) return;
        var choices = (await lookup.GetChoicesAsync(HttpContext.RequestAborted)).ToList();
        var saved = (await _assignments.GetListAsync(x => x.IsActive)).Select(x => x.GetPrimaryProgramScope()).ToArray();
        // Keep retired catalog choices selected. Dropping an unavailable choice could silently remove a restriction.
        foreach (var id in saved.SelectMany(x => x.MajorIds).Distinct().Except(choices.Select(x => x.Id)).ToArray())
            choices.Add(new(id, $"Unavailable major ({id})"));
        foreach (var id in saved.SelectMany(x => x.DegreeIds).Distinct().Except(choices.Where(x => x.DegreeId.HasValue).Select(x => x.DegreeId!.Value)).ToArray())
            choices.Add(new(0, "", id, $"Unavailable degree ({id})"));
        foreach (var id in saved.SelectMany(x => x.ProgramIds).Distinct().Except(choices.Where(x => x.BlockProgramId.HasValue).Select(x => x.BlockProgramId!.Value)).ToArray())
            choices.Add(new(0, "", BlockProgramId: id, BlockProgramName: $"Unavailable program ({id})"));
        ProgramChoices = choices;
    }

    private static IEnumerable<string> ReadStudentAttendanceTypes(string json)
    {
        try { return (JsonSerializer.Deserialize<CourseSelectionAttendanceTypeMapping[]>(json) ?? [])
            .Select(x => x.StudentAttendanceType).Where(x => !string.IsNullOrWhiteSpace(x)); }
        catch (JsonException) { return []; }
    }

    private static IEnumerable<string> ReadEmails(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Dictionary<string, bool> ReadGraduationOptions(string json)
    {
        try { return new Dictionary<string, bool>(JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [],
            StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); }
    }

    public class RoutingRow
    {
        [Required] public string AttendanceType { get; set; } = string.Empty;
        public string AdvisorEmailsJson { get; set; } = "[]";
        public string ProgramScopesJson { get; set; } = "{}";
        public string GraduationOptionsJson { get; set; } = "{}";
        public IReadOnlyList<SelectedAdvisor> Advisors { get; set; } = [];
    }

    public sealed record SelectedAdvisor(string Email, string Name, bool IncludeGraduatingStudents, PrimaryProgramScope Scope);
}
