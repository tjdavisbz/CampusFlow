using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Pages;
using CampusFlow.Web.Payments;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.Web.Tests.Payments;

public class BillingTermPickerTests
{
    [Theory]
    [InlineData("268",45)]
    [InlineData("269",5895)]
    public async Task Selecting_a_term_on_billing_updates_balance_and_payment_scope(string requested,decimal expected)
    {
        var fall=new StudentInformationSystemTerm(StudentInformationSystemProvider.ThesisElements,"268","B26Q","Fall 2026",new(2026,8,24),new(2026,12,11));
        var spring=fall with {ExternalTermId="269",TermCode="B27C",DisplayName="Spring 2027"};
        var terms=Substitute.For<IStudentInformationSystemTermLookup>();terms.Provider.Returns(fall.Provider);
        terms.GetTermsAsync(Arg.Any<CancellationToken>()).Returns(new[]{spring,fall});
        terms.GetCurrentTermAsync(Arg.Any<CancellationToken>()).Returns(spring);
        var profile=new StudentProfile(Guid.NewGuid(),null,null,new(fall.Provider,"10","100","student@example.test","Test",null,"Student"));
        var view=Substitute.For<ICurrentStudentView>();view.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        var billing=Substitute.For<IStudentInformationSystemBillingLookup>();billing.Provider.Returns(fall.Provider);
        StudentBillingTransaction Row(string id,string code,decimal amount)=>new(fall.Provider,id,id,code,code,DateTime.Today,"Activity",amount,0,amount,true,false);
        billing.GetTransactionsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{Row("268","B26Q",45),Row("269","B27C",5850),Row("270","B27Q",1000)});
        var context=new DefaultHttpContext();var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());
        var tenant=Substitute.For<ICurrentTenant>();var lazy=Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var preference=new StudentTermSelection(new HttpContextAccessor{HttpContext=context},tenant,user);
        var model=new BillingModel(Substitute.For<ITenantThemeProvider>(),Substitute.For<IRepository<StudentProfile,Guid>>(),view,
            new[]{billing},new[]{terms},NullLogger<BillingModel>.Instance,Options.Create(new PayflowOptions()),preference)
            {LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=context}};
        await model.OnGetAsync(requested);
        Assert.False(model.IsBillingUnavailable);
        Assert.Equal(requested,model.SelectedTermId);
        Assert.Equal(expected,model.CurrentBalance);
        Assert.Equal(6895m,model.OverallBalance);
        Assert.Equal(2,model.AvailableTerms.Count);
        Assert.Contains("CampusFlow.Term.",context.Response.Headers.SetCookie.ToString());
    }
}
