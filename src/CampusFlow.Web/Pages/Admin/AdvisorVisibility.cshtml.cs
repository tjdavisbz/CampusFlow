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

[Authorize(CampusFlowPermissions.AdvisorPortal.ManageRouting)]
public class AdvisorVisibilityModel : CampusFlowPageModel
{
    private readonly IRepository<AdvisorAssignment, Guid> _assignments;
    private readonly IRepository<RegistrationTermConfiguration, Guid> _termConfigurations;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IGuidGenerator _guidGenerator;

    public AdvisorVisibilityModel(IRepository<AdvisorAssignment, Guid> assignments,
        IRepository<RegistrationTermConfiguration, Guid> termConfigurations,
        IStudentInformationSystemAdvisorLookup advisorLookup, ITenantThemeProvider themeProvider,
        IGuidGenerator guidGenerator)
    {
        _assignments = assignments;
        _termConfigurations = termConfigurations;
        _advisorLookup = advisorLookup;
        _themeProvider = themeProvider;
        _guidGenerator = guidGenerator;
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
        if (Rows.GroupBy(x => x.AttendanceType, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            ModelState.AddModelError(string.Empty, "Each attendance type can appear only once.");

        var selections = new Dictionary<string, List<AdvisorLookupResult>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var emails = ReadEmails(row.AdvisorEmailsJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
                    .Select(x => new SelectedAdvisor(x.Email, x.DisplayName)).ToArray();
                row.AdvisorEmailsJson = JsonSerializer.Serialize(row.Advisors.Select(x => x.Email));
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
            foreach (var advisor in desired.Where(x => !currentEmails.Contains(x.Email)))
                await _assignments.InsertAsync(new AdvisorAssignment(_guidGenerator.Create(), CurrentTenant.Id,
                    row.AttendanceType, advisor.ExternalAdvisorId, advisor.Email, advisor.DisplayName, now),
                    autoSave: true);
        }
        Alerts.Success("Advisor assignments were saved.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        var active = await _assignments.GetListAsync(x => x.IsActive);
        var types = (await _termConfigurations.GetListAsync())
            .SelectMany(x => ReadStudentAttendanceTypes(x.AttendanceTypeMappingsJson))
            .Concat(active.Select(x => x.AttendanceType)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x).ToArray();
        Rows = types.Select(type =>
        {
            var advisors = active.Where(x => string.Equals(x.AttendanceType, type,
                StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.AdvisorDisplayName)
                .Select(x => new SelectedAdvisor(x.AdvisorEmail, x.AdvisorDisplayName)).ToArray();
            return new RoutingRow { AttendanceType = type, AdvisorEmailsJson =
                JsonSerializer.Serialize(advisors.Select(x => x.Email)), Advisors = advisors };
        }).ToList();
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

    public class RoutingRow
    {
        [Required] public string AttendanceType { get; set; } = string.Empty;
        public string AdvisorEmailsJson { get; set; } = "[]";
        public IReadOnlyList<SelectedAdvisor> Advisors { get; set; } = [];
    }

    public sealed record SelectedAdvisor(string Email, string Name);
}
