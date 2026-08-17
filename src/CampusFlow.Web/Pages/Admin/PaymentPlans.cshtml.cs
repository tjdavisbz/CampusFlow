using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace CampusFlow.Web.Pages.Admin;

[Authorize]
public class PaymentPlansModel : CampusFlowPageModel
{
    private readonly AdminPortalAccessService _adminAccess;
    private readonly IRepository<PaymentPlanPolicy, Guid> _policies;
    private readonly ITenantThemeProvider _themeProvider;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public PaymentPlansModel(AdminPortalAccessService adminAccess,
        IRepository<PaymentPlanPolicy, Guid> policies, ITenantThemeProvider themeProvider,
        IGuidGenerator guidGenerator, IClock clock)
    {
        _adminAccess = adminAccess;
        _policies = policies;
        _themeProvider = themeProvider;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    [BindProperty] public PaymentPlanSetupInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public bool Saved { get; set; }
    public int CurrentVersion { get; private set; }
    public DateTime? CurrentEffectiveFrom { get; private set; }
    public TenantTheme Theme { get; private set; } = new("CampusFlow", "#274690", "#172554", "#667eea",
        "#A1A8AE", "#F7F8FA", "#172033", null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await _adminAccess.EnsureAccessAsync("PaymentPlans")) return Forbid();
        Theme = _themeProvider.Get(CurrentTenant.Name);
        var current = await GetCurrentAsync();
        if (current is not null)
        {
            CurrentVersion = current.Version;
            CurrentEffectiveFrom = current.EffectiveFrom;
            Input = Map(current);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await _adminAccess.EnsureAccessAsync("PaymentPlans")) return Forbid();
        Theme = _themeProvider.Get(CurrentTenant.Name);
        if (!ModelState.IsValid)
        {
            var existing = await GetCurrentAsync();
            CurrentVersion = existing?.Version ?? 0;
            CurrentEffectiveFrom = existing?.EffectiveFrom;
            return Page();
        }

        var now = _clock.Now;
        var current = await GetCurrentAsync();
        if (current is not null)
        {
            current.Retire(now);
            await _policies.UpdateAsync(current);
        }

        await _policies.InsertAsync(new PaymentPlanPolicy(
            _guidGenerator.Create(), CurrentTenant.Id, Input.Name.Trim(), (current?.Version ?? 0) + 1, now,
            Input.EnrollmentFee, Input.PartTimeBalanceDivisor, Input.ResidentialMinimumPayment,
            Input.StandardMinimumPayment, SerializeLines(Input.ResidentialAttendanceTypes),
            SerializeLines(Input.FallDueDates), SerializeLines(Input.SpringDueDates),
            SerializeLines(Input.SummerDueDates), true), autoSave: true);

        return RedirectToPage(new { saved = true });
    }

    private async Task<PaymentPlanPolicy?> GetCurrentAsync()
    {
        var query = await _policies.GetQueryableAsync();
        return query.Where(x => x.IsPublished)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
    }

    private static PaymentPlanSetupInput Map(PaymentPlanPolicy policy) => new()
    {
        Name = policy.Name,
        EnrollmentFee = policy.EnrollmentFee,
        PartTimeBalanceDivisor = policy.PartTimeBalanceDivisor,
        ResidentialMinimumPayment = policy.ResidentialMinimumPayment,
        StandardMinimumPayment = policy.StandardMinimumPayment,
        ResidentialAttendanceTypes = JoinLines(policy.ResidentialAttendanceTypesJson),
        FallDueDates = JoinLines(policy.FallDueDatesJson),
        SpringDueDates = JoinLines(policy.SpringDueDatesJson),
        SummerDueDates = JoinLines(policy.SummerDueDatesJson)
    };

    private static string JoinLines(string json) =>
        string.Join(Environment.NewLine, JsonSerializer.Deserialize<string[]>(json) ?? []);

    private static string SerializeLines(string value) => JsonSerializer.Serialize(value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToArray());

    public sealed class PaymentPlanSetupInput
    {
        [Required, StringLength(128)] public string Name { get; set; } = "Standard Payment Plan";
        [Range(0, 10000)] public decimal EnrollmentFee { get; set; } = 100m;
        [Range(1, 24)] public decimal PartTimeBalanceDivisor { get; set; } = 3m;
        [Range(0, 100000)] public decimal ResidentialMinimumPayment { get; set; } = 3500m;
        [Range(0, 100000)] public decimal StandardMinimumPayment { get; set; } = 1500m;
        [Required] public string ResidentialAttendanceTypes { get; set; } = string.Empty;
        [Required] public string FallDueDates { get; set; } = string.Empty;
        [Required] public string SpringDueDates { get; set; } = string.Empty;
        [Required] public string SummerDueDates { get; set; } = string.Empty;
    }
}
