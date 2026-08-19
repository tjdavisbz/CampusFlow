using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Admin;

[Authorize(CampusFlowPermissions.Admin.PaymentPlans)]
public class PaymentPlansModel : CampusFlowPageModel
{
    private readonly IRepository<PaymentPlanPolicy, Guid> _policies;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public PaymentPlansModel(IRepository<PaymentPlanPolicy, Guid> policies, IGuidGenerator guidGenerator,
        ITenantThemeProvider tenantThemeProvider)
    {
        _policies = policies;
        _guidGenerator = guidGenerator;
        _tenantThemeProvider = tenantThemeProvider;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<PaymentPlanPolicy> History { get; private set; } = [];
    public TenantTheme Theme { get; private set; } = null!;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostPublishAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadHistoryAsync();
            return Page();
        }

        var now = Clock.Now;
        var all = await _policies.GetListAsync();
        foreach (var current in all.Where(x => x.IsPublished))
        {
            current.Retire(now);
            await _policies.UpdateAsync(current);
        }

        var nextVersion = all.Count == 0 ? 1 : all.Max(x => x.Version) + 1;
        var policy = new PaymentPlanPolicy(
            _guidGenerator.Create(), CurrentTenant.Id, Input.Name.Trim(), nextVersion, now,
            Input.EnrollmentFee, Input.PartTimeBalanceDivisor, Input.ResidentialMinimumPayment,
            Input.StandardMinimumPayment, SerializeLines(Input.ResidentialAttendanceTypes),
            SerializeLines(Input.FallDueDates), SerializeLines(Input.SpringDueDates),
            SerializeLines(Input.SummerDueDates), true);
        await _policies.InsertAsync(policy, autoSave: true);
        Alerts.Success($"Payment plan version {nextVersion} is now active.");
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        await LoadHistoryAsync();
        var current = History.FirstOrDefault(x => x.IsPublished);
        if (current is null) return;
        Input = new InputModel
        {
            Name = current.Name,
            EnrollmentFee = current.EnrollmentFee,
            PartTimeBalanceDivisor = current.PartTimeBalanceDivisor,
            ResidentialMinimumPayment = current.ResidentialMinimumPayment,
            StandardMinimumPayment = current.StandardMinimumPayment,
            ResidentialAttendanceTypes = DeserializeLines(current.ResidentialAttendanceTypesJson),
            FallDueDates = DeserializeLines(current.FallDueDatesJson),
            SpringDueDates = DeserializeLines(current.SpringDueDatesJson),
            SummerDueDates = DeserializeLines(current.SummerDueDatesJson)
        };
    }

    private async Task LoadHistoryAsync()
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        History = (await _policies.GetListAsync()).OrderByDescending(x => x.Version).ToArray();
    }

    private static string SerializeLines(string value) => JsonSerializer.Serialize(value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string DeserializeLines(string json) =>
        string.Join(Environment.NewLine, JsonSerializer.Deserialize<string[]>(json) ?? []);

    public class InputModel
    {
        [Required, StringLength(160)] public string Name { get; set; } = "Standard Payment Plan";
        [Range(0, 100000)] public decimal EnrollmentFee { get; set; }
        [Range(1, 24)] public decimal PartTimeBalanceDivisor { get; set; } = 3;
        [Range(0, 1000000)] public decimal ResidentialMinimumPayment { get; set; }
        [Range(0, 1000000)] public decimal StandardMinimumPayment { get; set; }
        [Required] public string ResidentialAttendanceTypes { get; set; } = string.Empty;
        [Required] public string FallDueDates { get; set; } = string.Empty;
        [Required] public string SpringDueDates { get; set; } = string.Empty;
        [Required] public string SummerDueDates { get; set; } = string.Empty;
    }
}
