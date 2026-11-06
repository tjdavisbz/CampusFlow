using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Students;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace CampusFlow.BillApprovals;

public class StudentTermSelectionTests
{
    private readonly IHttpContextAccessor _accessor=new HttpContextAccessor();
    private readonly ICurrentTenant _tenant=Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user=Substitute.For<ICurrentUser>();
    private readonly IStudentInformationSystemTermLookup _lookup=Substitute.For<IStudentInformationSystemTermLookup>();
    private readonly StudentProfile _profile;
    private readonly StudentTermSelection _selection;
    private static readonly StudentInformationSystemTerm Fall=new(StudentInformationSystemProvider.ThesisElements,"268","B26Q","Fall 2026",new(2026,8,24),new(2026,12,11));
    private static readonly StudentInformationSystemTerm Spring=new(StudentInformationSystemProvider.ThesisElements,"269","B27S","Spring 2027",new(2027,1,11),new(2027,4,30));
    public StudentTermSelectionTests()
    {
        var owner=Guid.NewGuid();_tenant.Id.Returns(owner);_user.Id.Returns(Guid.NewGuid());
        _profile=new(Guid.NewGuid(),owner,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
        _lookup.GetTermsAsync(Arg.Any<CancellationToken>()).Returns(new[]{Fall,Spring});
        _lookup.GetCurrentTermAsync(Arg.Any<CancellationToken>()).Returns(Fall);
        _selection=new(_accessor,_tenant,_user);Request();
    }
    private void Request(string? cookie=null)
    {
        var context=new DefaultHttpContext();context.Request.Scheme="https";
        if(cookie is not null)context.Request.Headers.Cookie=cookie;
        _accessor.HttpContext=context;
    }
    private string Cookie()=>_accessor.HttpContext!.Response.Headers.SetCookie.ToString().Split(';')[0];

    [Fact]
    public async Task Spring_choice_survives_navigation_and_dashboard_default_to_Fall()
    {
        Assert.Equal(Spring,await _selection.ResolveAsync(_profile,_lookup,"269","268"));
        var header=_accessor.HttpContext!.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly",header.ToLowerInvariant());Assert.Contains("secure",header.ToLowerInvariant());
        var cookie=Cookie();Request(cookie);
        Assert.Equal(Spring,await _selection.ResolveAsync(_profile,_lookup,defaultTermId:"268"));
        Request(cookie);Assert.Equal(Spring,await _selection.ResolveAsync(_profile,_lookup));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("viewed-student")]
    public async Task A_term_choice_does_not_leak_to_another_user_tenant_or_viewed_student(string change)
    {
        await _selection.ResolveAsync(_profile,_lookup,"269");var cookie=Cookie();Request(cookie);
        var profile=_profile;
        if(change=="user")_user.Id.Returns(Guid.NewGuid());
        if(change=="tenant")_tenant.Id.Returns(Guid.NewGuid());
        if(change=="viewed-student")profile=new(Guid.NewGuid(),_profile.TenantId,null,new(StudentInformationSystemProvider.ThesisElements,"11","101","other@example.test","Other",null,"Student"));
        Assert.Equal(Fall,await _selection.ResolveAsync(profile,_lookup));
    }

    [Fact]
    public async Task Unknown_or_stale_terms_cannot_replace_a_valid_selection()
    {
        await _selection.ResolveAsync(_profile,_lookup,"269");var cookie=Cookie();Request(cookie);
        Assert.Equal(Spring,await _selection.ResolveAsync(_profile,_lookup,"not-a-term"));
        _lookup.GetTermsAsync(Arg.Any<CancellationToken>()).Returns(new[]{Fall});Request(cookie);
        Assert.Equal(Fall,await _selection.ResolveAsync(_profile,_lookup));
    }
}
