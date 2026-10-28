using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.FinancialAid;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Admin.FinancialAid;

[Authorize(CampusFlowPermissions.Admin.ReconcileAwardCodes)]
public class ReconcileAwardCodesModel : CampusFlowPageModel
{
    private readonly IReconcileAwardCodesAppService _service;
    private readonly ITenantThemeProvider _themeProvider;

    public ReconcileAwardCodesModel(IReconcileAwardCodesAppService service, ITenantThemeProvider themeProvider)
    {
        _service = service;
        _themeProvider = themeProvider;
    }

    [BindProperty(SupportsGet = true)] public string? Term { get; set; }
    [BindProperty] public ApplyInputModel Input { get; set; } = new();
    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<string> Terms { get; private set; } = [];
    public IReadOnlyList<AwardCodeMismatch> Awards { get; private set; } = [];
    public bool HasMore => Awards.Count > ReconcileAwardCodesAppService.BatchSize;
    public bool CanApply { get; private set; }
    public IReadOnlyList<AwardCodeMismatch> VisibleAwards => Awards.Take(ReconcileAwardCodesAppService.BatchSize).ToList();
    public string PreviewIds => string.Join(',', VisibleAwards.Select(x => x.AwardId));

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostApplyAsync()
    {
        Term = Input.Term;
        if (!Input.Confirmed)
            ModelState.AddModelError("Input.Confirmed", "Confirm that you intend to update these awards.");
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        var result = await _service.ApplyAsync(Input.Term, Input.PreviewAwardIds);
        if (result.Error is not null) Alerts.Warning(result.Error);
        else Alerts.Success($"Updated {result.Updated} financial award(s) for {Input.Term}.");
        return RedirectToPage(new { term = Input.Term });
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        CanApply = await _service.CanApplyAsync();
        Terms = await _service.GetTermsAsync();
        if (!string.IsNullOrWhiteSpace(Term)) Awards = await _service.PreviewAsync(Term);
    }

    public class ApplyInputModel
    {
        [Required] public string Term { get; set; } = string.Empty;
        [Required] public string PreviewAwardIds { get; set; } = string.Empty;
        public bool Confirmed { get; set; }
    }
}
