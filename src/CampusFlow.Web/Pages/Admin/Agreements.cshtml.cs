using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.BillApproval)]
public class AgreementsModel : CampusFlowPageModel
{
    private static readonly string[] AllowedMergeFields = ["StudentName", "StudentId", "TermName", "AcceptedAt"];
    private readonly IRepository<AgreementTemplate, Guid> _agreements;
    private readonly IRepository<BillApproval, Guid> _billApprovals;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IGuidGenerator _guidGenerator;

    public AgreementsModel(IRepository<AgreementTemplate, Guid> agreements, IRepository<BillApproval, Guid> billApprovals,
        ITenantThemeProvider themeProvider, IGuidGenerator guidGenerator)
    {
        _agreements = agreements; _billApprovals = billApprovals; _themeProvider = themeProvider; _guidGenerator = guidGenerator;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<AgreementTemplate> History { get; private set; } = [];
    public AgreementTemplate? ViewedAgreement { get; private set; }
    public int ViewedAgreementAcceptanceCount { get; private set; }

    public async Task OnGetAsync(Guid? copyFrom = null, Guid? view = null)
    {
        await LoadAsync();
        if (view.HasValue)
        {
            ViewedAgreement = History.FirstOrDefault(x => x.Id == view.Value);
            if (ViewedAgreement is not null)
                ViewedAgreementAcceptanceCount = await _billApprovals.CountAsync(x =>
                    x.AgreementTemplateId == ViewedAgreement.Id && x.AcceptedAt != null);
        }
        var source = copyFrom.HasValue ? await _agreements.GetAsync(copyFrom.Value) : History.FirstOrDefault(x => x.IsPublished);
        if (source is not null) Input = new InputModel { Name = source.Name, ContentHtml = source.ContentHtml };
    }

    public async Task<IActionResult> OnPostPublishAsync()
    {
        if (ContainsUnsafeMarkup(Input.ContentHtml))
            ModelState.AddModelError("Input.ContentHtml", "Scripts, embedded content, event handlers, and javascript links are not allowed.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        var now = Clock.Now;
        var all = await _agreements.GetListAsync();
        foreach (var current in all.Where(x => x.IsPublished))
        {
            current.Retire(now);
            await _agreements.UpdateAsync(current);
        }
        var version = all.Count == 0 ? 1 : all.Max(x => x.Version) + 1;
        await _agreements.InsertAsync(new AgreementTemplate(_guidGenerator.Create(), CurrentTenant.Id,
            Input.Name.Trim(), version, now, Input.ContentHtml.Trim(), JsonSerializer.Serialize(AllowedMergeFields), true), autoSave: true);
        Alerts.Success($"Agreement version {version} is now available for Bill Approval terms.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        History = (await _agreements.GetListAsync()).OrderByDescending(x => x.Version).ToArray();
    }

    private static bool ContainsUnsafeMarkup(string html) =>
        Regex.IsMatch(html, @"<\s*(script|iframe|object|embed|form|input|button)\b|\son\w+\s*=|javascript\s*:", RegexOptions.IgnoreCase);

    public class InputModel
    {
        [Required, StringLength(160)] public string Name { get; set; } = "Student Bill Agreement";
        [Required, MinLength(20)] public string ContentHtml { get; set; } = "<h3>Agreement</h3><p>Enter the agreement terms here.</p>";
    }
}
