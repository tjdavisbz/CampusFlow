using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Announcements;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Pages;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalEnrollmentDashboardTests
{
    private static readonly StudentInformationSystemTerm Fall = new(StudentInformationSystemProvider.ThesisElements,
        "268", "B26Q", "Fall 2026", new(2026, 8, 24), new(2026, 12, 11));
    private static readonly StudentInformationSystemTerm Spring = new(StudentInformationSystemProvider.ThesisElements,
        "269", "B27C", "Spring 2027", new(2027, 1, 11), new(2027, 4, 30));
    private readonly IndexModel _page;
    private readonly BillApprovalTermConfiguration _bill;
    private readonly RegistrationTermConfiguration _registration;
    private readonly ICourseSelectionAppService _courses = Substitute.For<ICourseSelectionAppService>();
    private readonly IStudentInformationSystemScheduleLookup _schedule = Substitute.For<IStudentInformationSystemScheduleLookup>();
    private readonly IStudentInformationSystemBillingLookup _billing = Substitute.For<IStudentInformationSystemBillingLookup>();
    private readonly IMealPlanAppService _meals = Substitute.For<IMealPlanAppService>();
    private readonly IBillApprovalCompletionLookup _completion = Substitute.For<IBillApprovalCompletionLookup>();
    private readonly IStudentTermSelection _termSelection = Substitute.For<IStudentTermSelection>();
    private readonly IStudentInformationSystemTermLookup _terms = Substitute.For<IStudentInformationSystemTermLookup>();
    private readonly StudentProfile _profile;
    private StudentInformationSystemTerm _selected = Spring;

    public BillApprovalEnrollmentDashboardTests()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(tenantId); tenant.Name.Returns("nelson");
        var user = Substitute.For<ICurrentUser>(); user.Id.Returns(Guid.NewGuid());
        var clock = Substitute.For<IClock>(); clock.Now.Returns(new DateTime(2026, 11, 5));
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        lazy.LazyGetRequiredService<IClock>().Returns(clock);
        _profile = new(Guid.NewGuid(), tenantId, null,
            new(StudentInformationSystemProvider.ThesisElements, "10", "100", "student@example.test", "Test", null, "Student"));
        var studentView = Substitute.For<ICurrentStudentView>(); studentView.IsImpersonating.Returns(true);
        studentView.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(_profile);
        _terms.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _terms.GetTermsAsync(Arg.Any<CancellationToken>()).Returns(new[] { Fall, Spring });
        _termSelection.ResolveAsync(_profile, _terms, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string?>(2) == Spring.ExternalTermId ? Spring : _selected);
        _registration = Registration(Spring, tenantId);
        var fallRegistration = Registration(Fall, tenantId); fallRegistration.ConfigureDashboard(true, true);
        var registrationRepo = Substitute.For<IRepository<RegistrationTermConfiguration, Guid>>();
        registrationRepo.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<RegistrationTermConfiguration> { fallRegistration, _registration });
        _bill = new(Guid.NewGuid(), tenantId, "269", "B27C", "Spring 2027", new(2026, 11, 4), new(2027, 3, 17),
            false, Guid.NewGuid(), Guid.NewGuid());
        _bill.ConfigureTestAccess(true, ["100"]);
        var billRepo = Substitute.For<IRepository<BillApprovalTermConfiguration, Guid>>();
        billRepo.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<BillApprovalTermConfiguration> { _bill });
        var announcements = Substitute.For<IRepository<PortalAnnouncement, Guid>>();
        announcements.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<PortalAnnouncement>());
        _schedule.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _schedule.GetScheduleAsync("10", Arg.Any<CancellationToken>()).Returns(Enumerable.Range(1, 4).Select(Course).ToArray());
        _billing.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _billing.GetTransactionsAsync("10", Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentBillingTransaction>());
        _courses.GetEligibleTermsAsync().Returns(new List<CourseSelectionTermDto>());
        _meals.GetAsync(Arg.Any<string>()).Returns(new MealPlanSelectionDto { HousingRequired = false });
        _page = new IndexModel(Substitute.For<ITenantThemeProvider>(), Substitute.For<IRepository<StudentProfile, Guid>>(),
            studentView, [], [_terms], null!, NullLogger<IndexModel>.Instance, null!, null!, null!, null!, null!, null!,
            _courses, registrationRepo, _meals, Substitute.For<IRepository<BillApproval, Guid>>(), billRepo, [_billing],
            [_schedule], new ConfigurationBuilder().Build(), announcements, null!, _completion, _termSelection)
        { LazyServiceProvider = lazy, PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };
    }

    private static RegistrationTermConfiguration Registration(StudentInformationSystemTerm term, Guid tenant) =>
        new(Guid.NewGuid(), tenant, term.ExternalTermId, term.TermCode, term.DisplayName,
            new(2026, 11, 11), new(2027, 4, 30), true, true, true, "[]");
    private static StudentCourseScheduleItem Course(int id) => new(StudentInformationSystemProvider.ThesisElements,
        id.ToString(), id.ToString(), "269", "B27C", "Spring 2027", "TEST", id.ToString(), "", "01", "Test course", 3,
        "Unofficial", null, null, "Instructor", null, null, null, null, null, null, null);
    private IndexModel.RegistrationJourneyStep Step(string title) => _page.RegistrationJourney!.Steps.Single(x => x.Title == title);

    [Fact]
    public async Task Existing_twelve_hours_complete_course_selection_and_allow_test_bill_review_before_registration_opens()
    {
        await _page.OnGetAsync("269");
        var journey = Assert.IsType<IndexModel.RegistrationJourneyViewModel>(_page.RegistrationJourney);
        Assert.Equal(4, journey.SelectedCourseCount); Assert.Equal(12m, journey.SelectedCredits);
        Assert.False(journey.CourseSelectionEnabled); Assert.Equal("complete", Step("Course selection").State);
        Assert.Equal("/Schedule?term=269", Step("Course selection").Url);
        Assert.True(journey.BillApprovalEnabled); Assert.Equal("/BillApproval?term=B27C", Step("Bill approval").Url);
        await _courses.DidNotReceiveWithAnyArgs().GetAsync(default!);
    }
    [Fact]
    public async Task Default_Fall_dashboard_exposes_the_allowed_Spring_bill_without_claiming_Fall_courses_are_complete()
    {
        _selected = Fall; await _page.OnGetAsync();
        Assert.Equal(Fall, _page.CurrentTerm);
        Assert.Equal("269", Assert.Single(_page.OtherBillApprovalTerms).ExternalTermId);
        Assert.Equal("B27C", Assert.Single(_page.OtherBillApprovalTerms).TermCode);
        Assert.Equal(0, _page.RegistrationJourney!.SelectedCourseCount);
        Assert.Equal("disabled", Step("Course selection").State);
        Assert.Null(Step("Bill approval").Url);
    }
    [Fact]
    public async Task Open_bill_term_remains_selectable_when_course_dashboard_term_is_hidden()
    {
        _registration.ConfigureDashboard(false, false); _selected = Fall;
        await _page.OnGetAsync("269");
        Assert.Equal(Spring, _page.CurrentTerm);
        Assert.Single(_page.DashboardTerms, x => x.ExternalTermId == "269");
        Assert.Equal("action", Step("Bill approval").State);
    }
    [Fact]
    public async Task Unlisted_students_do_not_get_a_test_bill_link_or_extra_dashboard_term()
    {
        _bill.ConfigureTestAccess(true, ["200"]); _registration.ConfigureDashboard(false, false); _selected = Fall;
        await _page.OnGetAsync();
        Assert.Empty(_page.OpenBillApprovalTerms); Assert.Empty(_page.OtherBillApprovalTerms);
        Assert.DoesNotContain(_page.DashboardTerms, x => x.ExternalTermId == "269");
        Assert.Null(Step("Bill approval").Url);
    }
    [Fact]
    public async Task Publicly_enabled_bill_term_works_without_test_access_or_open_course_selection()
    {
        _bill.ConfigureTestAccess(false, []);
        _bill.Update(new(2026, 11, 4), new(2027, 3, 17), true, _bill.AgreementTemplateId, _bill.PaymentPlanPolicyId);
        await _page.OnGetAsync("269");
        Assert.Equal("action", Step("Bill approval").State); Assert.Single(_page.OpenBillApprovalTerms);
    }
    [Fact]
    public async Task Closed_course_selection_without_existing_courses_stays_incomplete()
    {
        _schedule.GetScheduleAsync("10", Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentCourseScheduleItem>());
        await _page.OnGetAsync("269");
        Assert.Equal("disabled", Step("Course selection").State); Assert.Null(Step("Course selection").Url);
    }
    [Fact]
    public async Task Empty_course_selection_response_cannot_erase_schedule_enrollment()
    {
        _courses.GetEligibleTermsAsync().Returns(new List<CourseSelectionTermDto> { new() { ExternalTermId = "269" } });
        _courses.GetAsync("269").Returns(new CourseSelectionDto());
        await _page.OnGetAsync("269");
        Assert.True(_page.RegistrationJourney!.HasSelectedCourses); Assert.Equal("complete", Step("Course selection").State);
        Assert.Equal("/CourseSelection?term=269", Step("Course selection").Url);
    }
    [Fact]
    public async Task Advisor_review_is_still_waiting_when_courses_have_pending_review()
    {
        _courses.GetEligibleTermsAsync().Returns(new List<CourseSelectionTermDto> { new() { ExternalTermId = "269" } });
        _courses.GetAsync("269").Returns(new CourseSelectionDto { Registrations = [new() { NeedsReview = true }] });
        await _page.OnGetAsync("269"); Assert.Equal("waiting", Step("Course selection").State);
    }
    [Theory] [InlineData(50, false)] [InlineData(50.01, true)]
    public async Task Existing_previous_balance_threshold_is_preserved(decimal balance, bool blocked)
    {
        _billing.GetPreviousBalanceAsync("10", "269", Arg.Any<CancellationToken>()).Returns(balance);
        await _page.OnGetAsync("269");
        Assert.Equal(blocked, Step("Bill approval").Url is null); Assert.Equal("complete", Step("Course selection").State);
    }
    [Fact]
    public async Task Required_housing_is_still_enforced_for_test_access()
    {
        _meals.GetAsync("269").Returns(new MealPlanSelectionDto { HousingRequired = true, HasHousingAssignment = false });
        await _page.OnGetAsync("269");
        Assert.Null(Step("Bill approval").Url); Assert.Equal("Housing required", Step("Bill approval").ActionLabel);
    }
}
