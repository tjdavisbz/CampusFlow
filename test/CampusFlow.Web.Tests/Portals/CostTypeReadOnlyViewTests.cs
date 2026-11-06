using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Pages.Admin.StudentBilling;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.Portals;
public class CostTypeReadOnlyViewTests
{
    [Theory] [InlineData("Save")] [InlineData("Preview")] [InlineData("Run")]
    public async Task Student_view_keeps_cost_type_writes_blocked(string handler)
    {
        var (context,session,permissions)=Request("POST","/Admin/StudentBilling/CostTypes");
        context.Request.QueryString=new QueryString("?handler="+handler);bool reached=false;
        await new StudentViewContextMiddleware(_=>{reached=true;return Task.CompletedTask;})
            .InvokeAsync(context,session,permissions,NullLogger<StudentViewContextMiddleware>.Instance);
        Assert.False(reached);Assert.Equal(403,context.Response.StatusCode);
        context.Response.Body.Position=0;
        Assert.Contains("Student impersonation is read-only",await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
    [Fact]
    public async Task Readonly_cost_page_can_load_and_identifies_the_active_student_view()
    {
        var (context,session,permissions)=Request("GET","/Admin/StudentBilling/CostTypes");bool reached=false;
        await new StudentViewContextMiddleware(_=>{reached=true;return Task.CompletedTask;})
            .InvokeAsync(context,session,permissions,NullLogger<StudentViewContextMiddleware>.Instance);
        Assert.True(reached);
        var page=new CostTypesModel(Substitute.For<ICostTypeAppService>()){PageContext=new PageContext{HttpContext=context}};
        Assert.True(page.IsReadOnlyView);
    }
    [Fact]
    public async Task Exit_student_view_remains_available_to_restore_normal_admin_work()
    {
        var (context,session,permissions)=Request("POST","/Admin/ImpersonateStudent/End");bool reached=false;
        await new StudentViewContextMiddleware(_=>{reached=true;return Task.CompletedTask;})
            .InvokeAsync(context,session,permissions,NullLogger<StudentViewContextMiddleware>.Instance);
        Assert.True(reached);
    }
    [Theory] [InlineData(StudentViewClaimTypes.ExternalStudentId,true)]
    [InlineData(FacultyViewClaimTypes.FacultyId,true)] [InlineData("unrelated",false)]
    public void Page_warning_follows_student_and_faculty_view_claims(string type,bool expected)
    {
        var context=new DefaultHttpContext();context.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(type,"10")],"test"));
        var page=new CostTypesModel(Substitute.For<ICostTypeAppService>()){PageContext=new PageContext{HttpContext=context}};
        Assert.Equal(expected,page.IsReadOnlyView);
    }
    private static (DefaultHttpContext,StudentViewSession,IPermissionChecker) Request(string method,string path)
    {
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());
        var session=new StudentViewSession(new EphemeralDataProtectionProvider(),user,tenant);
        var start=new DefaultHttpContext();session.Start(start,new(StudentInformationSystemProvider.ThesisElements,"10","100","test@example.test","Test",null,"Account"));
        var context=new DefaultHttpContext();context.Request.Method=method;context.Request.Path=path;
        context.Request.Headers.Cookie=start.Response.Headers.SetCookie.ToString().Split(';')[0];context.Response.Body=new MemoryStream();
        var permissions=Substitute.For<IPermissionChecker>();permissions.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default).Returns(true);
        return (context,session,permissions);
    }
}
