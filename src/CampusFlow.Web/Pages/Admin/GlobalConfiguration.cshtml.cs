using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.GlobalConfiguration)]
public class GlobalConfigurationModel : CampusFlowPageModel
{
    private readonly IRepository<RegistrationTermConfiguration, Guid> _terms;
    private readonly ITenantThemeProvider _themeProvider;

    public GlobalConfigurationModel(IRepository<RegistrationTermConfiguration, Guid> terms,
        ITenantThemeProvider themeProvider)
    {
        _terms = terms;
        _themeProvider = themeProvider;
    }

    [BindProperty] public Guid? CurrentDashboardTermId { get; set; }
    [BindProperty] public List<Guid> StudentSelectableTermIds { get; set; } = [];
    public IReadOnlyList<RegistrationTermConfiguration> Terms { get; private set; } = [];
    public TenantTheme Theme { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        await LoadAsync();
        CurrentDashboardTermId = Terms.FirstOrDefault(x => x.IsDashboardDefault)?.Id;
        StudentSelectableTermIds = Terms.Where(x => x.IsStudentSelectable).Select(x => x.Id).ToList();
    }

    public async Task<IActionResult> OnPostSaveTermsAsync()
    {
        await LoadAsync();
        var current = CurrentDashboardTermId.HasValue
            ? Terms.FirstOrDefault(x => x.Id == CurrentDashboardTermId.Value)
            : null;
        if (current is null)
            ModelState.AddModelError(nameof(CurrentDashboardTermId), "Choose the term students should see first.");
        if (!ModelState.IsValid) return Page();

        var selectableIds = StudentSelectableTermIds.ToHashSet();
        selectableIds.Add(current!.Id);
        foreach (var term in Terms)
        {
            term.ConfigureDashboard(selectableIds.Contains(term.Id), term.Id == current.Id);
            await _terms.UpdateAsync(term, autoSave: true);
        }
        Alerts.Success($"{current.TermName} is now the current student dashboard term.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        Terms = (await _terms.GetListAsync()).OrderByDescending(x => x.TermCode).ToArray();
    }
}
