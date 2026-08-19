using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.BillApproval)]
public class BillApprovalModel : CampusFlowPageModel
{
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _configurations;
    private readonly IRepository<AgreementTemplate, Guid> _agreements;
    private readonly IRepository<PaymentPlanPolicy, Guid> _paymentPlans;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IGuidGenerator _guidGenerator;

    public BillApprovalModel(IRepository<BillApprovalTermConfiguration, Guid> configurations,
        IRepository<AgreementTemplate, Guid> agreements, IRepository<PaymentPlanPolicy, Guid> paymentPlans,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups, ITenantThemeProvider themeProvider,
        IGuidGenerator guidGenerator)
    {
        _configurations = configurations; _agreements = agreements; _paymentPlans = paymentPlans;
        _termLookups = termLookups.ToArray(); _themeProvider = themeProvider; _guidGenerator = guidGenerator;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<BillApprovalTermConfiguration> Configurations { get; private set; } = [];
    public IReadOnlyList<SelectListItem> Terms { get; private set; } = [];
    public IReadOnlyList<SelectListItem> Agreements { get; private set; } = [];
    public IReadOnlyList<SelectListItem> PaymentPlans { get; private set; } = [];

    public async Task OnGetAsync(Guid? edit = null, Guid? copyFrom = null)
    {
        await LoadListsAsync();
        var sourceId = edit ?? copyFrom;
        if (!sourceId.HasValue) return;
        var source = await _configurations.GetAsync(sourceId.Value);
        Input = new InputModel { Id = source.Id, ExternalTermId = source.ExternalTermId,
            OpensAt = source.OpensAt, ClosesAt = source.ClosesAt, IsEnabled = source.IsEnabled,
            AgreementTemplateId = source.AgreementTemplateId, PaymentPlanPolicyId = source.PaymentPlanPolicyId };
        if (copyFrom.HasValue) { Input.Id = null; Input.ExternalTermId = string.Empty; Input.IsEnabled = false; }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadListsAsync();
        if (Terms.All(x => x.Value != Input.ExternalTermId)) ModelState.AddModelError("Input.ExternalTermId", "Choose a term from Elements.");
        if (Input.ClosesAt <= Input.OpensAt) ModelState.AddModelError("Input.ClosesAt", "Closing must be after opening.");
        if (Agreements.All(x => x.Value != Input.AgreementTemplateId.ToString())) ModelState.AddModelError("Input.AgreementTemplateId", "Choose an agreement.");
        if (PaymentPlans.All(x => x.Value != Input.PaymentPlanPolicyId.ToString())) ModelState.AddModelError("Input.PaymentPlanPolicyId", "Choose a payment plan.");
        if (Configurations.Any(x => x.ExternalTermId == Input.ExternalTermId && x.Id != Input.Id)) ModelState.AddModelError("Input.ExternalTermId", "That term already has Bill Approval settings.");
        if (!ModelState.IsValid) return Page();
        var term = (await GetTermsAsync()).Single(x => x.ExternalTermId == Input.ExternalTermId);
        if (Input.Id.HasValue)
        {
            var existing = await _configurations.GetAsync(Input.Id.Value);
            existing.Update(Input.OpensAt, Input.ClosesAt, Input.IsEnabled, Input.AgreementTemplateId, Input.PaymentPlanPolicyId);
            await _configurations.UpdateAsync(existing, autoSave: true);
        }
        else await _configurations.InsertAsync(new BillApprovalTermConfiguration(_guidGenerator.Create(), CurrentTenant.Id,
            term.ExternalTermId, term.TermCode, term.DisplayName, Input.OpensAt, Input.ClosesAt,
            Input.IsEnabled, Input.AgreementTemplateId, Input.PaymentPlanPolicyId), autoSave: true);
        Alerts.Success($"{term.DisplayName} Bill Approval settings were saved.");
        return RedirectToPage();
    }

    private async Task LoadListsAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        Configurations = (await _configurations.GetListAsync()).OrderByDescending(x => x.TermCode).ToArray();
        Terms = (await GetTermsAsync()).Select(x => new SelectListItem(x.DisplayName, x.ExternalTermId)).ToArray();
        Agreements = (await _agreements.GetListAsync()).OrderByDescending(x => x.Version)
            .Select(x => new SelectListItem($"{x.Name} · Version {x.Version}", x.Id.ToString())).ToArray();
        PaymentPlans = (await _paymentPlans.GetListAsync()).OrderByDescending(x => x.Version)
            .Select(x => new SelectListItem($"{x.Name} · Version {x.Version}", x.Id.ToString())).ToArray();
    }
    private Task<IReadOnlyList<StudentInformationSystemTerm>> GetTermsAsync() => _termLookups
        .Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements).GetTermsAsync(HttpContext.RequestAborted);

    public class InputModel
    {
        public Guid? Id { get; set; }
        [Required] public string ExternalTermId { get; set; } = string.Empty;
        public DateTime OpensAt { get; set; } = DateTime.Today;
        public DateTime ClosesAt { get; set; } = DateTime.Today.AddMonths(3);
        public bool IsEnabled { get; set; }
        public Guid AgreementTemplateId { get; set; }
        public Guid PaymentPlanPolicyId { get; set; }
    }
}
