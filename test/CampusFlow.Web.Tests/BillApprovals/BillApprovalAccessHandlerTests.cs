using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Branding;
using CampusFlow.Housing;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.BillApprovals;
using CampusFlow.Web.Pages;
using CampusFlow.Web.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalAccessHandlerTests
{
    private readonly CampusFlow.Web.Pages.BillApprovalModel _model;
    private readonly IMealPlanAppService _meal=Substitute.For<IMealPlanAppService>();
    private readonly IPayflowGateway _gateway=Substitute.For<IPayflowGateway>();
    private readonly IStudentInformationSystemBillingLookup _billing=Substitute.For<IStudentInformationSystemBillingLookup>();
    private readonly BillApprovalTermConfiguration _term;
    private readonly IRepository<BillApproval,Guid> _approvals=Substitute.For<IRepository<BillApproval,Guid>>();

    public BillApprovalAccessHandlerTests()
    {
        var owner=Guid.NewGuid();var userId=Guid.NewGuid();var user=Substitute.For<ICurrentUser>();user.Id.Returns(userId);
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(owner);tenant.Name.Returns("nelson");
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var current=Substitute.For<ICurrentStudentView>();
        current.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(new StudentProfile(Guid.NewGuid(),owner,userId,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student")));
        var termLookup=Substitute.For<IStudentInformationSystemTermLookup>();termLookup.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        termLookup.GetCurrentTermAsync(Arg.Any<CancellationToken>()).Returns(new StudentInformationSystemTerm(StudentInformationSystemProvider.ThesisElements,"269","B27S","Spring 2027",new(2027,1,11),new(2027,4,30)));
        var schedule=Substitute.For<IStudentInformationSystemScheduleLookup>();schedule.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        schedule.GetScheduleAsync("10",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentCourseScheduleItem>());
        _billing.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentBillingTransaction>());
        var aid=Substitute.For<IStudentInformationSystemFinancialAidLookup>();aid.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentFinancialAidAward>());
        var clock=Substitute.For<IClock>();clock.Now.Returns(new DateTime(2026,11,5));
        _term=new(Guid.NewGuid(),owner,"269","B27S","Spring 2027",new(2026,12,1),new(2027,2,1),false,Guid.NewGuid(),Guid.NewGuid());
        var terms=Substitute.For<IRepository<BillApprovalTermConfiguration,Guid>>();terms.GetListAsync(Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<BillApprovalTermConfiguration>{_term});
        _meal.GetAsync("269").Returns(new MealPlanSelectionDto{ExternalTermId="269",HousingRequired=false});
        var theme=Substitute.For<ITenantThemeProvider>();
        _model=new(theme,Substitute.For<IRepository<StudentProfile,Guid>>(),current,Array.Empty<IStudentInformationSystemStudentLookup>(),
            new[]{termLookup},new[]{schedule},new[]{_billing},new[]{aid},Array.Empty<IStudentInformationSystemPaymentPlanLookup>(),
            Array.Empty<IStudentInformationSystemDocumentTrackingService>(),Array.Empty<IStudentInformationSystemPaymentPostingService>(),
            new ConfigurationBuilder().Build(),Substitute.For<IRepository<AgreementTemplate,Guid>>(),Substitute.For<IRepository<PaymentPlanPolicy,Guid>>(),
            terms,_approvals,Substitute.For<IRepository<BillApprovalArtifact,Guid>>(),Substitute.For<IBlobContainer<BillApprovalPdfContainer>>(),
            Substitute.For<IBillApprovalPdfGenerator>(),Substitute.For<IGuidGenerator>(),clock,Substitute.For<IRepository<PayflowPayment,Guid>>(),
            _gateway,Options.Create(new PayflowOptions()),NullLogger<CampusFlow.Web.Pages.BillApprovalModel>.Instance,
            Substitute.For<ILegacyBillApprovalLookup>(),_meal,Substitute.For<IBillApprovalCompletionLookup>())
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()}};
    }

    [Fact]
    public async Task An_unlisted_student_cannot_open_a_test_only_term_or_reach_meal_selection()
    {
        _term.ConfigureTestAccess(true,new[]{"200"});
        await _model.OnGetAsync("B27S");
        Assert.True(_model.IsUnavailable);Assert.Empty(_model.AvailableTerms);
        Assert.Empty(_meal.ReceivedCalls());
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(50.01, true)]
    public async Task Test_access_obeys_the_previous_balance_gate(double balance,bool blocked)
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        _billing.GetPreviousBalanceAsync("10","269",Arg.Any<CancellationToken>()).Returns((decimal)balance);
        await _model.OnGetAsync("B27S");
        Assert.Equal(blocked,_model.IsUnavailable);
        if(blocked)Assert.Contains("previous balance",_model.UnavailableReason);
        await _meal.Received(1).GetAsync("269");
    }

    [Fact]
    public async Task Direct_payment_submission_cannot_bypass_a_closed_term()
    {
        await _model.OnPostPaymentAsync("B27S");
        Assert.True(_model.IsUnavailable);Assert.False(_model.ModelState.IsValid);
        Assert.Empty(_gateway.ReceivedCalls());
        await _approvals.DidNotReceiveWithAnyArgs().InsertAsync(default!,default,default);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_approval_and_payment_submissions_cannot_bypass_a_blocking_previous_balance(bool payment)
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        _billing.GetPreviousBalanceAsync("10","269",Arg.Any<CancellationToken>()).Returns(100m);
        if(payment)await _model.OnPostPaymentAsync("B27S");
        else await _model.OnPostAsync("B27S");
        Assert.True(_model.IsUnavailable);
        Assert.Empty(_gateway.ReceivedCalls());
        await _approvals.DidNotReceiveWithAnyArgs().InsertAsync(default!,default,default);
    }

}
