using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Bibliu;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.BibliuConfiguration)]
public class BibliuModel(IRepository<BibliuConfiguration, Guid> configurations,
    IEnumerable<IStudentInformationSystemTermLookup> lookups, IGuidGenerator guids,
    ITenantThemeProvider themes) : CampusFlowPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<StudentInformationSystemTerm> Terms { get; private set; } = [];
    public IReadOnlyList<string> ExpandedTerms { get; private set; } = [];
    public TenantTheme Theme { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentTenant.Id.HasValue) return Forbid();
        await LoadAsync();
        var config = await configurations.FindAsync(x => x.TenantId == CurrentTenant.Id);
        if (config is not null) Input = new() { BillingTermId=config.BillingTermId, BibliuTermId=config.BibliuTermId,
            StoreNumber=config.StoreNumber, InSiteStoreNumber=config.InSiteStoreNumber,
            IsEnabled=config.IsEnabled, ConcurrencyStamp=config.ConcurrencyStamp };
        Expand();
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!CurrentTenant.Id.HasValue) return Forbid();
        await LoadAsync();
        if (!Terms.Any(x=>x.ExternalTermId==Input.BillingTermId)) ModelState.AddModelError("Input.BillingTermId","Choose a billing term from Elements.");
        if (!Terms.Any(x=>x.ExternalTermId==Input.BibliuTermId)) ModelState.AddModelError("Input.BibliuTermId","Choose a BibliU term from Elements.");
        Expand();
        if (!ModelState.IsValid) return Page();
        var config = await configurations.FindAsync(x => x.TenantId == CurrentTenant.Id);
        if ((config?.ConcurrencyStamp ?? "") != (Input.ConcurrencyStamp ?? ""))
        {
            ModelState.AddModelError("", "These settings changed since you opened the page. Reload before saving.");
            return Page();
        }
        var isNew = config is null;
        config ??= new(guids.Create(), CurrentTenant.Id.Value);
        config.Update(Input.BillingTermId, Input.BibliuTermId, Input.StoreNumber, Input.InSiteStoreNumber, Input.IsEnabled);
        if (isNew) await configurations.InsertAsync(config, autoSave:true);
        else await configurations.UpdateAsync(config, autoSave:true);
        Alerts.Success("BibliU settings saved. Production Power Automate flows have not been changed.");
        return RedirectToPage();
    }
    private async Task LoadAsync()
    {
        Theme=themes.Get(CurrentTenant.Name);
        Terms=await lookups.Single(x=>x.Provider==StudentInformationSystemProvider.ThesisElements).GetTermsAsync(HttpContext.RequestAborted);
    }
    private void Expand()
    {
        var term=Terms.SingleOrDefault(x=>x.ExternalTermId==Input.BibliuTermId);
        if(term is null) return;
        try { ExpandedTerms=BibliuRules.ExpandedTerms(term.DisplayName).Where(n=>Terms.Any(t=>t.DisplayName==n)).ToArray(); }
        catch(ArgumentException) { ModelState.AddModelError("Input.BibliuTermId","Choose a Spring, Summer or Fall term."); }
    }
    public sealed class InputModel
    {
        [Required] public string BillingTermId { get; set; } = "";
        [Required] public string BibliuTermId { get; set; } = "";
        [Required, RegularExpression("^[0-9]{3}$")] public string StoreNumber { get; set; } = "123";
        [Required, RegularExpression("^[0-9]{3}$")] public string InSiteStoreNumber { get; set; } = "241";
        public bool IsEnabled { get; set; }
        public string? ConcurrencyStamp { get; set; }
    }
}
