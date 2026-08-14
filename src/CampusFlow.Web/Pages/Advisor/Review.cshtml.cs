using System;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.Branding;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Advisor;

[Authorize(CampusFlowPermissions.AdvisorPortal.Default)]
public class ReviewModel : CampusFlowPageModel
{
    private readonly IAdvisorPortalAppService _advisorPortal;
    private readonly ITenantThemeProvider _tenantThemeProvider;

    public ReviewModel(IAdvisorPortalAppService advisorPortal, ITenantThemeProvider tenantThemeProvider)
    {
        _advisorPortal = advisorPortal;
        _tenantThemeProvider = tenantThemeProvider;
    }

    public TenantTheme Theme { get; private set; } = new(
        "CampusFlow", "#274690", "#172554", "#667eea", "#A1A8AE", "#F7F8FA", "#172033",
        null, null, "system-ui, sans-serif", "system-ui, sans-serif", null);
    public AdvisorStudentReviewDto Review { get; private set; } = new();

    [BindProperty]
    public SubmitAdvisorReviewInput Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(Guid studentProfileId, string term) =>
        await LoadAsync(studentProfileId, term);

    public async Task<IActionResult> OnPostAsync()
    {
        await _advisorPortal.SubmitAsync(Input);
        SuccessMessage = "The advisor review was recorded.";
        return RedirectToPage("/Advisor/Index");
    }

    private async Task LoadAsync(Guid studentProfileId, string term)
    {
        Theme = _tenantThemeProvider.Get(CurrentTenant.Name);
        Review = await _advisorPortal.GetStudentReviewAsync(studentProfileId, term);
        Input.StudentProfileId = studentProfileId;
        Input.ExternalTermId = term;
        Input.Courses.Clear();
        foreach (var course in Review.Courses)
        {
            Input.Courses.Add(new AdvisorCourseDecisionInput
            {
                ReviewId = course.ReviewId,
                Comment = course.AdvisorComment
            });
        }
    }
}
