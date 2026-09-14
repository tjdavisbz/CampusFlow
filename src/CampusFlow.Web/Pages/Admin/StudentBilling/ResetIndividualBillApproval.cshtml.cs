using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Admin.StudentBilling;

[Authorize(CampusFlowPermissions.Admin.ResetIndividualBillApproval)]
public class ResetIndividualBillApprovalModel : CampusFlowPageModel
{
    private readonly IResetBillApprovalAppService _service;
    private readonly ITenantThemeProvider _themeProvider;

    public ResetIndividualBillApprovalModel(IResetBillApprovalAppService service,
        ITenantThemeProvider themeProvider)
    {
        _service = service;
        _themeProvider = themeProvider;
    }

    [BindProperty(SupportsGet = true)] public string? TermCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? PreviewId { get; set; }
    [BindProperty] public ResetInputModel ResetInput { get; set; } = new();

    public TenantTheme Theme { get; private set; } = null!;
    public IReadOnlyList<ResetBillApprovalTermDto> Terms { get; private set; } = [];
    public IReadOnlyList<ResetBillApprovalDto> Approvals { get; private set; } = [];
    public ResetBillApprovalDto? Preview { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (PreviewId.HasValue)
            Preview = await _service.GetAsync(PreviewId.Value);
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!ResetInput.Confirmed)
            ModelState.AddModelError("ResetInput.Confirmed", "Confirm that you intend to reset this student's Bill Approval.");

        if (!ModelState.IsValid)
        {
            PreviewId = ResetInput.ApprovalId;
            await LoadAsync();
            Preview = await _service.GetAsync(ResetInput.ApprovalId);
            return Page();
        }

        var record = await _service.GetAsync(ResetInput.ApprovalId);
        await _service.ResetAsync(ResetInput.ApprovalId);
        Alerts.Success($"Bill Approval was reset for {record.StudentName} ({record.StudentId}) for {record.TermName}.");
        return RedirectToPage(new { termCode = record.TermCode, query = record.StudentId });
    }

    private async Task LoadAsync()
    {
        Theme = _themeProvider.Get(CurrentTenant.Name);
        var result = await _service.SearchAsync(TermCode, Query);
        Terms = result.Terms;
        Approvals = result.Approvals;
    }

    public class ResetInputModel
    {
        [Required] public Guid ApprovalId { get; set; }
        [Range(typeof(bool), "true", "true", ErrorMessage = "Confirmation is required.")]
        public bool Confirmed { get; set; }
    }
}
