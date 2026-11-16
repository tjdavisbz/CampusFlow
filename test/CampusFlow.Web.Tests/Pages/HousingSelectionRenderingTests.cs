using System;
using System.IO;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Pages;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Xunit;

namespace CampusFlow.Pages;

public class HousingSelectionRenderingTests : CampusFlowWebTestBase
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Recorded_plan_remains_visible_when_approved_or_selection_closed(bool approved, bool open)
    {
        var selection = Selection();
        selection.IsLockedByBillApproval = approved;
        selection.IsSelectionOpen = open;
        selection.UnavailableReason = "Selection is closed for this term.";
        var (html, unavailable) = await Render(selection);
        Assert.True(unavailable);
        Assert.Contains("15 Meal Plan", html);
        Assert.Contains("Spring 2027", html);
        Assert.Contains("housing-selection-summary", html);
        Assert.Contains(approved ? "Your bill is approved" : selection.UnavailableReason, html);
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("Continue with bill approval", html);
    }

    [Fact]
    public async Task Explicit_no_plan_choice_remains_visible_after_approval()
    {
        var selection = Selection();
        selection.SelectedMealPlanName = null;
        selection.SelectedMealPlanId = null;
        selection.IsLockedByBillApproval = true;
        var (html, _) = await Render(selection);
        Assert.Contains("No meal plan", html);
        Assert.DoesNotContain("<form", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_selection_keeps_existing_choice_and_form_behavior(bool selected)
    {
        var selection = Selection();
        if (!selected) selection.SelectedHousingChoice = null;
        var (html, unavailable) = await Render(selection);
        Assert.False(unavailable);
        Assert.Equal(selected, html.Contains("housing-selection-summary"));
        Assert.Equal(selected, html.Contains("Continue with bill approval"));
        Assert.Equal(!selected, html.Contains("<form"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_pending_removal_selection_does_not_show_a_recorded_plan(bool pending)
    {
        var selection = pending ? Selection() : null;
        if (selection is not null)
        {
            selection.SelectedHousingChoice = null;
            selection.HasPendingRemoval = true;
            selection.IsSelectionOpen = false;
            selection.UnavailableReason = "Meal plan removal is pending.";
        }
        var (html, unavailable) = await Render(selection);
        Assert.True(unavailable);
        Assert.DoesNotContain("housing-selection-summary", html);
        Assert.DoesNotContain("<form", html);
    }

    private static MealPlanSelectionDto Selection() => new()
    {
        ExternalTermId = "269", TermName = "Spring 2027", StudentName = "Test Student", StudentId = "TEST",
        HousingRequired = true, HasHousingAssignment = true, MealPlanEligible = true, IsSelectionOpen = true,
        AssignedHousingChoice = HousingChoice.OnCampus, SelectedHousingChoice = HousingChoice.OnCampus,
        SelectedMealPlanId = 15, SelectedMealPlanName = "15 Meal Plan",
        Options = { [HousingChoice.OnCampus] = [new() { ExternalMealPlanId = 15, Name = "15 Meal Plan", Description = "Semester plan", Price = 100m }] }
    };

    private async Task<(string Html, bool Unavailable)> Render(MealPlanSelectionDto? selection)
    {
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var services = scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = services };
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Name.Returns("nelson");
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var meals = Substitute.For<IMealPlanAppService>();
        meals.GetAsync(Arg.Any<string?>()).Returns(Task.FromResult(selection!));
        var themes = Substitute.For<ITenantThemeProvider>();
        var page = new HousingModel(themes, meals, Substitute.For<IRepository<BillApprovalTermConfiguration, Guid>>(),
            Substitute.For<IClock>(), NullLogger<HousingModel>.Instance, Substitute.For<IElementsHousingReader>(),
            Substitute.For<ICurrentStudentView>(), Substitute.For<IStudentTermSelection>(), Array.Empty<IStudentInformationSystemTermLookup>())
        { LazyServiceProvider = lazy, PageContext = new PageContext { HttpContext = http } };
        themes.Get("nelson").Returns(page.Theme);
        await page.OnGetAsync();
        var routes = new RouteData();
        var router = Substitute.For<IRouter>();
        router.GetVirtualPath(Arg.Any<VirtualPathContext>()).Returns(new VirtualPathData(router, "/"));
        routes.Routers.Add(router);
        routes.Values["page"] = "/Housing";
        var context = new ActionContext(http, routes, new ActionDescriptor());
        var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, "/Pages/Housing.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary<HousingModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = page };
        var razorPage = Assert.IsAssignableFrom<Page>(((RazorView)view.View).RazorPage);
        razorPage.PageContext = new PageContext(context) { ViewData = data };
        using var output = new StringWriter();
        await view.View.RenderAsync(new ViewContext(context, view.View, data,
            new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()), output, new HtmlHelperOptions()));
        await meals.DidNotReceiveWithAnyArgs().SaveAsync(default!);
        return (output.ToString(), page.IsUnavailable);
    }
}
