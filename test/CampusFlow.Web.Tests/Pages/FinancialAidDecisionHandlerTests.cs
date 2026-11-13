using System;using System.Collections.Generic;using System.Security.Claims;using System.Threading;using System.Threading.Tasks;using CampusFlow.Branding;using CampusFlow.Students;using CampusFlow.StudentInformationSystems;using CampusFlow.Web.Pages;using CampusFlow.Web.Students;using Microsoft.AspNetCore.Http;using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;using Microsoft.Extensions.Logging.Abstractions;using NSubstitute;using Volo.Abp.DependencyInjection;using Volo.Abp.Domain.Repositories;using Volo.Abp.Users;using Xunit;
namespace CampusFlow.Pages;
public class FinancialAidDecisionHandlerTests
{
    static(FinancialAidModel page,IStudentInformationSystemFinancialAidDecisionService service) Setup(string? claim=null,bool impersonating=false,bool locked=false)
    {
        var view=Substitute.For<ICurrentStudentView>();view.IsImpersonating.Returns(impersonating);view.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(new StudentProfile(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student")));
        var l=Substitute.For<IStudentInformationSystemFinancialAidLookup>();l.Provider.Returns(StudentInformationSystemProvider.ThesisElements);l.GetAwardsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{FinancialAidBatchTests.Award() with{IsLocked=locked}});
        var s=Substitute.For<IStudentInformationSystemFinancialAidDecisionService>();s.Provider.Returns(StudentInformationSystemProvider.ThesisElements);s.GetDecisionsAsync("10",Arg.Any<CancellationToken>()).Returns(new Dictionary<string,int?>{{"1",null}});
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());var lazy=Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);var tenant=Substitute.For<Volo.Abp.MultiTenancy.ICurrentTenant>();tenant.Name.Returns("nelson");lazy.LazyGetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Returns(tenant);
        var page=new FinancialAidModel(Substitute.For<ITenantThemeProvider>(),Substitute.For<IRepository<StudentProfile,Guid>>(),view,new[]{l},new[]{s},Array.Empty<IStudentInformationSystemTermLookup>(),NullLogger<FinancialAidModel>.Instance,Substitute.For<IStudentTermSelection>()){LazyServiceProvider=lazy,PageContext=new PageContext{HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(claim is null?Array.Empty<Claim>():new[]{new Claim(claim,"test")},"test"))}}};return(page,s);
    }
    [Theory] [InlineData("campusflow:student-view:external-id")] [InlineData("CampusFlow.SchedulerView.Email")] [InlineData("CampusFlow.FacultyView.FacultyId")]
    public async Task Impersonation_claims_cannot_submit_single_or_bulk(string claim)
    {var(p,s)=Setup(claim);Assert.True(p.IsReadOnlyView);Assert.IsType<ForbidResult>(await p.OnPostDecisionAsync("1",StudentFinancialAidDecision.Accept));Assert.IsType<ForbidResult>(await p.OnPostAcceptAllAsync("269","1",true));Assert.Empty(s.ReceivedCalls());}
    [Fact] public async Task Current_view_readonly_and_missing_confirmation_prevent_writes()
    {var(p,s)=Setup(impersonating:true);Assert.IsType<ForbidResult>(await p.OnPostAcceptAllAsync("269","1",true));Assert.Empty(s.ReceivedCalls());var(p2,s2)=Setup();Assert.IsType<ForbidResult>(await p2.OnPostAcceptAllAsync("269","1",false));Assert.Empty(s2.ReceivedCalls());}
    [Fact] public async Task Individual_decision_redirect_retains_the_awards_semester()
    {var(p,s)=Setup();var result=Assert.IsType<RedirectToPageResult>(await p.OnPostDecisionAsync("1",StudentFinancialAidDecision.Accept));Assert.Equal("B27C",result.RouteValues!["openTerm"]);await s.Received(1).SubmitDecisionAsync("10","1",StudentFinancialAidDecision.Accept,Arg.Any<CancellationToken>(),"269");}
    [Theory][InlineData(StudentFinancialAidDecision.Accept)][InlineData(StudentFinancialAidDecision.Decline)]
    public async Task A_locked_award_can_be_decided_individually(StudentFinancialAidDecision decision)
    {var(p,s)=Setup(locked:true);Assert.IsType<RedirectToPageResult>(await p.OnPostDecisionAsync("1",decision));Assert.False(p.StatusIsError);await s.Received(1).SubmitDecisionAsync("10","1",decision,Arg.Any<CancellationToken>(),"269");}
    [Fact] public async Task Opened_historical_semester_is_restored_after_redirect()
    {var(p,s)=Setup();await p.OnGetAsync(openTerm:"B27C");Assert.Equal("B27C",p.OpenTermCode);Assert.Single(p.HistoricalTerms);await p.OnGetAsync(openTerm:"unknown");Assert.Null(p.OpenTermCode);}
    [Fact] public async Task Bulk_redirect_retains_the_reviewed_semester()
    {var(p,s)=Setup();var result=Assert.IsType<RedirectToPageResult>(await p.OnPostAcceptAllAsync("269","1",true,"B27C"));Assert.Equal("B27C",result.RouteValues!["openTerm"]);Assert.False(p.StatusIsError);}
}
