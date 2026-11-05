using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Pages.Payments;
using CampusFlow.Web.Payments;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.Web.Tests.Payments;

public class TermAwareCheckoutTests
{
    private readonly CheckoutModel _model;
    private readonly IStudentInformationSystemBillingLookup _billing=Substitute.For<IStudentInformationSystemBillingLookup>();
    private readonly IStudentInformationSystemPaymentPostingService _posting=Substitute.For<IStudentInformationSystemPaymentPostingService>();
    private readonly IRepository<PayflowPayment,Guid> _payments=Substitute.For<IRepository<PayflowPayment,Guid>>();
    private readonly IPayflowGateway _gateway=Substitute.For<IPayflowGateway>();
    private static readonly StudentInformationSystemTerm Fall=new(StudentInformationSystemProvider.ThesisElements,"268","B26Q","Fall 2026",new(2026,8,24),new(2026,12,11));
    public TermAwareCheckoutTests()
    {
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());
        var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        var profile=new StudentProfile(Guid.NewGuid(),null,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
        var view=Substitute.For<ICurrentStudentView>();view.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        _billing.Provider.Returns(StudentInformationSystemProvider.ThesisElements);_posting.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        var terms=Substitute.For<IStudentInformationSystemTermLookup>();terms.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        terms.GetCurrentTermAsync(Arg.Any<CancellationToken>()).Returns(Fall with {ExternalTermId="269",TermCode="B27S",DisplayName="Spring 2027"});
        var preference=Substitute.For<IStudentTermSelection>();
        preference.ResolveAsync(Arg.Any<StudentProfile>(),terms,Arg.Any<string?>(),Arg.Any<string?>(),Arg.Any<CancellationToken>()).Returns(Fall);
        _payments.GetListAsync(Arg.Any<Expression<Func<PayflowPayment,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<PayflowPayment>());
        _posting.PostAsync(Arg.Any<string>(),Arg.Any<int>(),Arg.Any<decimal>(),Arg.Any<decimal>(),Arg.Any<string>(),Arg.Any<bool>(),Arg.Any<DateTime>(),Arg.Any<CancellationToken>()).Returns(new StudentPaymentPostingResult(1,2,null));
        _model=new(view,new[]{_billing},new[]{terms},new[]{_posting},_payments,_gateway,
            Options.Create(new PayflowOptions{Enabled=true,TestMode=true,Vendor="fake",Password="fake"}),NullLogger<CheckoutModel>.Instance,preference)
        {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext()}};
        Activity(100,900);
    }
    private void Activity(decimal current,decimal future)=>_billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{
        new StudentBillingTransaction(StudentInformationSystemProvider.ThesisElements,"1","268","B26Q","Fall 2026",DateTime.Today,"Tuition",current,0,current,false,false),
        new StudentBillingTransaction(StudentInformationSystemProvider.ThesisElements,"2","269","B27S","Spring 2027",DateTime.Today,"Tuition",future,0,future,true,false)});

    [Theory]
    [InlineData(false,100)]
    [InlineData(true,1000)]
    public async Task Default_payment_matches_the_explicit_balance_scope(bool includeFuture,int expected)
    {
        _model.IncludeFutureTerms=includeFuture;await _model.OnGetAsync();
        Assert.Equal((decimal)expected,_model.Amount);Assert.Equal(1000m,_model.OverallBalance);
    }

    [Fact]
    public async Task A_paid_current_term_does_not_prefill_next_terms_charges()
    {
        Activity(0,3380);await _model.OnGetAsync();Assert.Equal(0m,_model.Amount);Assert.Equal(3380m,_model.OverallBalance);
    }

    [Fact]
    public async Task A_payment_above_the_selected_scope_is_rejected_before_the_gateway()
    {
        _model.Amount=1000;_model.ExpirationMonth=12;_model.ExpirationYear=2099;await _model.OnPostAsync();
        Assert.False(_model.ModelState.IsValid);Assert.Empty(_gateway.ReceivedCalls());
    }

    [Theory]
    [InlineData(true,268)]
    [InlineData(false,269)]
    public async Task Retry_uses_the_retained_term_and_preserves_the_old_fallback_only_for_legacy_records(bool retained,int expected)
    {
        var payment=new PayflowPayment(Guid.NewGuid(),null,Guid.NewGuid(),Guid.NewGuid(),"10",50,0,"USD","request",true);
        if(retained)payment.SetElementsTerm(268);payment.Complete(true,0,"Approved","fake-reference");
        _payments.GetListAsync(Arg.Any<Expression<Func<PayflowPayment,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<PayflowPayment>{payment});
        await _model.OnGetAsync();
        await _posting.Received(1).PostAsync("10",expected,50,0,"fake-reference",true,Arg.Any<DateTime>(),Arg.Any<CancellationToken>());
    }
}
