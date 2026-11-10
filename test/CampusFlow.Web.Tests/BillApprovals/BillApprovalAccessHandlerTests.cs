using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using System.Text.Json;
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
    private readonly IStudentInformationSystemBillingRecalculationService _recalculation=Substitute.For<IStudentInformationSystemBillingRecalculationService>();
    private readonly IPayflowGateway _gateway=Substitute.For<IPayflowGateway>();
    private readonly IStudentInformationSystemBillingLookup _billing=Substitute.For<IStudentInformationSystemBillingLookup>();
    private readonly BillApprovalTermConfiguration _term;
    private readonly IStudentInformationSystemFinancialAidLookup _aid=Substitute.For<IStudentInformationSystemFinancialAidLookup>();
    private readonly IStudentInformationSystemFinancialAidDecisionService _aidDecisions=Substitute.For<IStudentInformationSystemFinancialAidDecisionService>();
    private readonly IStudentInformationSystemPaymentPlanLookup _planLookup=Substitute.For<IStudentInformationSystemPaymentPlanLookup>();
    private readonly IRepository<AgreementTemplate,Guid> _agreements=Substitute.For<IRepository<AgreementTemplate,Guid>>();
    private readonly IRepository<PaymentPlanPolicy,Guid> _policies=Substitute.For<IRepository<PaymentPlanPolicy,Guid>>();
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
        var aid=_aid;aid.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _aidDecisions.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _aidDecisions.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(new Dictionary<string,int?>());
        aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentFinancialAidAward>());
        var clock=Substitute.For<IClock>();clock.Now.Returns(new DateTime(2026,11,5));
        _term=new(Guid.NewGuid(),owner,"269","B27S","Spring 2027",new(2026,12,1),new(2027,2,1),false,Guid.NewGuid(),Guid.NewGuid());
        var terms=Substitute.For<IRepository<BillApprovalTermConfiguration,Guid>>();terms.GetListAsync(Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<BillApprovalTermConfiguration>{_term});
        _planLookup.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _recalculation.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _meal.GetAsync("269").Returns(new MealPlanSelectionDto{ExternalTermId="269",HousingRequired=false});
        var theme=Substitute.For<ITenantThemeProvider>();
        _model=new(theme,Substitute.For<IRepository<StudentProfile,Guid>>(),current,Array.Empty<IStudentInformationSystemStudentLookup>(),
            new[]{termLookup},new[]{schedule},new[]{_billing},new[]{aid},new[]{_planLookup},
            Array.Empty<IStudentInformationSystemDocumentTrackingService>(),Array.Empty<IStudentInformationSystemPaymentPostingService>(),
            new ConfigurationBuilder().Build(),_agreements,_policies,
            terms,_approvals,Substitute.For<IRepository<BillApprovalArtifact,Guid>>(),Substitute.For<IBlobContainer<BillApprovalPdfContainer>>(),
            Substitute.For<IBillApprovalPdfGenerator>(),Substitute.For<IGuidGenerator>(),clock,Substitute.For<IRepository<PayflowPayment,Guid>>(),
            _gateway,Options.Create(new PayflowOptions()),NullLogger<CampusFlow.Web.Pages.BillApprovalModel>.Instance,
            Substitute.For<ILegacyBillApprovalLookup>(),_meal,Substitute.For<IBillApprovalCompletionLookup>(), Substitute.For<CampusFlow.Web.Students.IStudentTermSelection>(), new[]{_recalculation}, Substitute.For<Volo.Abp.BackgroundJobs.IBackgroundJobManager>(),new[]{_aidDecisions})
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()}};
    }

    [Fact]
    public async Task Bill_rows_totals_and_review_fingerprint_use_the_same_aid_policy_and_live_decisions()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        StudentFinancialAidAward A(string id,string type,string status,decimal amount,bool loan,int? decision=null)=>
            new(StudentInformationSystemProvider.ThesisElements,id,"269","B27S","Spring 2027",new DateTime(2027,1,26),type,status,type,amount,false,decision,null,loan);
        var rows=new[]{A("1","TEG","Guarantee",300,false),A("2","Loan","Guarantee",100,true),A("3","Gift","Awarded",200,false),A("4","FWS","Guarantee",999,false),A("5","TEG","Estimated",888,false),A("6","Private loan","Guarantee",777,true)};
        _aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(rows);
        _aidDecisions.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(new Dictionary<string,int?>{{"1",null},{"2",1},{"3",null},{"4",1},{"5",1},{"6",null}});
        await _model.OnGetAsync("B27S");Assert.False(_model.IsUnavailable);Assert.Equal(600,_model.PendingAidTotal);Assert.Equal(new[]{"1","3","2"},_model.Aid.Select(x=>x.ExternalAwardId));var fingerprint=_model.CurrentReviewVersion;
        _aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(rows.Select(x=>x.ExternalAwardId=="4"?x with{Amount=50000}:x).ToArray());
        await _model.OnGetAsync("B27S");Assert.Equal(fingerprint,_model.CurrentReviewVersion);Assert.Equal(600,_model.PendingAidTotal);
    }
    [Fact]
    public async Task Pending_loans_use_live_decisions_and_selected_term_without_changing_bill_totals_or_fingerprint()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        StudentFinancialAidAward Loan(string id,string term="269") =>
            new(StudentInformationSystemProvider.ThesisElements,id,term,"B27S","Spring 2027",null,"Loan","Awarded","Loan "+id,500,false,null,null,true);
        var rows=new[]{Loan("pending"),Loan("accepted"),Loan("declined"),Loan("other-term","268"),Loan("gift") with {IsLoan=false},Loan("cancelled") with {AwardStatus="Cancelled"},Loan("billed") with {IsSentToBilling=true}};
        _aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(rows);
        var decisions=new Dictionary<string,int?>{{"pending",null},{"accepted",1},{"declined",0},{"gift",null},{"cancelled",null},{"billed",null}};
        _aidDecisions.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(decisions);
        await _model.OnGetAsync("B27S");
        Assert.False(_model.IsUnavailable);
        Assert.Equal("pending",Assert.Single(_model.PendingLoans).ExternalAwardId);
        Assert.Equal(1000,_model.PendingAidTotal);Assert.Equal(-1000,_model.RemainingBalance);
        var fingerprint=_model.CurrentReviewVersion;
        _aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(rows.Select(x=>x.ExternalAwardId=="pending"?x with {Amount=9000}:x).ToArray());
        await _model.OnGetAsync("B27S");
        Assert.Equal(9000,Assert.Single(_model.PendingLoans).Amount);
        Assert.Equal(1000,_model.PendingAidTotal);Assert.Equal(fingerprint,_model.CurrentReviewVersion);
        decisions["pending"]=1;
        await _model.OnGetAsync("B27S");
        Assert.Empty(_model.PendingLoans);Assert.Equal(10000,_model.PendingAidTotal);
        Assert.NotEqual(fingerprint,_model.CurrentReviewVersion);
        Assert.DoesNotContain(_aidDecisions.ReceivedCalls(),x=>x.GetMethodInfo().Name=="SubmitDecisionAsync");
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Missing_live_aid_decision_stops_bill_review_instead_of_using_a_stale_loan_state()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});_aid.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentFinancialAidAward(StudentInformationSystemProvider.ThesisElements,"1","269","B27S","Spring 2027",null,"Loan","Guarantee","Loan",100,false,1,null,true)});
        await _model.OnGetAsync("B27S");Assert.True(_model.IsUnavailable);Assert.Empty(_gateway.ReceivedCalls());
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

    [Fact]
    public async Task Recalculation_completes_before_the_bill_is_read()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        await _model.OnGetAsync("B27S");
        Assert.False(_model.IsUnavailable);
        Received.InOrder(()=>
        {
            _recalculation.RecalculateAsync("10","269",Arg.Any<Guid>(),Arg.Any<CancellationToken>());
            _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Recalculation_failure_stops_the_review_without_reading_a_stale_ledger()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        _recalculation.RecalculateAsync("10","269",Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Still processing")));
        await _model.OnGetAsync("B27S");
        Assert.True(_model.IsUnavailable);
        await _billing.DidNotReceive().GetTransactionsAsync("10",Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_changed_review_cannot_be_paid_without_reviewing_the_updated_amounts()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        await _model.OnGetAsync("B27S");_model.ReviewVersion=_model.CurrentReviewVersion;
        _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentBillingTransaction(StudentInformationSystemProvider.ThesisElements,"1","269","B27S","Spring 2027",DateTime.Today,"Tuition",1000,0,1000,true,false)});
        await _model.OnPostPaymentAsync("B27S");
        Assert.False(_model.ModelState.IsValid);Assert.Empty(_gateway.ReceivedCalls());
        Assert.NotEqual(_model.ReviewVersion,_model.CurrentReviewVersion);
    }

    [Fact]
    public async Task Accepted_deferred_bill_records_the_configured_fee_and_fee_inclusive_remaining_balance()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentBillingTransaction(StudentInformationSystemProvider.ThesisElements,"1","269","B27S","Spring 2027",DateTime.Today,"Tuition",1000,0,1000,true,false)});
        _planLookup.GetPaymentPlanContextAsync("10","269",Arg.Any<CancellationToken>()).Returns(new StudentPaymentPlanContext("Full Time","Graduate",true));
        var policy=new PaymentPlanPolicy(_term.PaymentPlanPolicyId,null,"Test",1,DateTime.Today,125,3,0,0,"[]","[]","[\"February 28\",\"March 30\"]","[]",true);
        _policies.GetQueryableAsync().Returns(new[]{policy}.AsQueryable());
        _agreements.GetAsync(_term.AgreementTemplateId,Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new AgreementTemplate(_term.AgreementTemplateId,null,"Test",1,DateTime.Today,"Agreement","[]",true));
        BillApproval? saved=null;
        _approvals.InsertAsync(Arg.Do<BillApproval>(row=>saved=row),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(c=>c.Arg<BillApproval>());
        await _model.OnGetAsync("B27S");Assert.False(_model.IsUnavailable);
        Assert.Equal(125m,_model.PaymentPlanFee);Assert.Equal(1125m,_model.DeferredBalance);
        _model.ReviewVersion=_model.CurrentReviewVersion;_model.AgreementAcceptedInput=true;_model.PaymentChoiceInput="Deferred";
        await _model.OnPostAsync("B27S");
        Assert.NotNull(saved);Assert.Equal(125m,saved.PaymentPlanFee);Assert.Equal(1125m,saved.RemainingBalance);
        Assert.Equal(1125m,JsonSerializer.Deserialize<List<StudentPaymentInstallment>>(saved.PaymentScheduleJson)!.Sum(x=>x.Amount));
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task Replaced_billing_row_identifiers_with_identical_reviewed_values_do_not_make_a_review_stale()
    {
        _term.ConfigureTestAccess(true,new[]{"100"});
        var row=new StudentBillingTransaction(StudentInformationSystemProvider.ThesisElements,"1","269","B27S","Spring 2027",DateTime.Today,"Tuition",1000,0,1000,true,false);
        _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{row});
        await _model.OnGetAsync("B27S");var version=_model.CurrentReviewVersion;
        _billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{row with {ExternalTransactionId="replacement"}});
        await _model.OnGetAsync("B27S");Assert.Equal(version,_model.CurrentReviewVersion);
    }

}
