using System;
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
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Xunit;

namespace CampusFlow.Pages;

public class MealPlanChoiceHandlerTests
{
    private readonly IMealPlanAppService _service = Substitute.For<IMealPlanAppService>();
    private readonly HousingModel _page;

    public MealPlanChoiceHandlerTests()
    {
        _service.GetAsync("269").Returns(new MealPlanSelectionDto
        {
            ExternalTermId = "269", HousingRequired = true, HasHousingAssignment = true,
            MealPlanEligible = true, IsSelectionOpen = true, AssignedHousingChoice = HousingChoice.Commuter,
            Options = { [HousingChoice.Commuter] = [new() { IsNoPlanOption = true, Name = "None" }] }
        });
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(Substitute.For<ICurrentTenant>());
        _page = new(Substitute.For<ITenantThemeProvider>(), _service,
            Substitute.For<IRepository<BillApprovalTermConfiguration, Guid>>(), Substitute.For<IClock>(),
            NullLogger<HousingModel>.Instance, Substitute.For<IElementsHousingReader>(),
            Substitute.For<ICurrentStudentView>(), Substitute.For<IStudentTermSelection>(), [])
        {
            LazyServiceProvider = lazy, PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Term = "269", Input = new() { ExternalTermId = "269", HousingChoice = HousingChoice.Commuter }
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-plan")]
    public async Task Missing_or_invalid_choice_is_rejected_even_with_a_forged_hidden_plan_id(string? choice)
    {
        _page.MealPlanChoice = choice; _page.Input.ExternalMealPlanId = 20; _page.Input.SelectNoMealPlan = true;
        Assert.IsType<PageResult>(await _page.OnPostAsync());
        Assert.False(_page.ModelState.IsValid);
        await _service.DidNotReceiveWithAnyArgs().SaveAsync(default!);
    }

    [Theory]
    [InlineData("none", true, null)]
    [InlineData("20", false, 20)]
    public async Task Explicit_choice_is_sent_with_unambiguous_intent_and_preserves_the_term(string choice, bool none, int? planId)
    {
        _page.MealPlanChoice = choice;
        var result = Assert.IsType<RedirectToPageResult>(await _page.OnPostAsync());
        await _service.Received(1).SaveAsync(Arg.Is<SaveMealPlanSelectionInput>(x =>
            x.SelectNoMealPlan == none && x.ExternalMealPlanId == planId && x.ExternalTermId == "269"));
        Assert.Equal("269", result.RouteValues!["term"]);
        Assert.Equal(true, result.RouteValues["saved"]);
    }

    [Fact]
    public async Task Loading_the_form_does_not_preselect_None()
    {
        await _page.OnGetAsync();
        Assert.Null(_page.MealPlanChoice);
        Assert.False(_page.Input.SelectNoMealPlan);
    }
}
