using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class StudentOnlyPortalMiddlewareTests
{
    [Theory]
    [InlineData("terryjdavis@my.nelson.edu", true)]
    [InlineData("STUDENT@MY.NELSON.EDU", true)]
    [InlineData("tjdavis@nelson.edu", false)]
    public void Nelson_Student_Mailbox_Is_Not_A_Staff_Login(string email, bool expected)
    {
        StudentOnlyAccountAccess.IsNelsonStudentMailbox(email).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/Admin/ImpersonateFaculty", true)]
    [InlineData("/Advisor", true)]
    [InlineData("/Faculty/students", true)]
    [InlineData("/Identity/Users", true)]
    [InlineData("/api/app/advisor-portal/queue", true)]
    [InlineData("/api/app/housing-workspace", true)]
    [InlineData("/api/app/new-staff-service", true)]
    [InlineData("/api/permission-management/permissions", true)]
    [InlineData("/", false)]
    [InlineData("/DegreeAudit", false)]
    [InlineData("/CourseSelection", false)]
    [InlineData("/api/app/course-selection", false)]
    [InlineData("/api/app/meal-plan", false)]
    public void Privileged_Routes_Are_Identified(string path, bool expected)
    {
        StudentOnlyPortalMiddleware.IsPrivilegedPath(new PathString(path)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/Admin/ImpersonateFaculty", true)]
    [InlineData("/Advisor", true)]
    [InlineData("/Identity/Users", true)]
    [InlineData("/api/app/housing-workspace", true)]
    [InlineData("/api/permission-management/permissions", true)]
    [InlineData("/Faculty", false)]
    [InlineData("/Faculty/students", false)]
    [InlineData("/Faculty/email", false)]
    [InlineData("/api/app/course-selection", false)]
    [InlineData("/", false)]
    public void Only_Staff_Accounts_Can_Reach_Non_Faculty_Privileged_Routes(string path, bool expected)
    {
        StudentOnlyPortalMiddleware.IsStaffOnlyPath(new PathString(path)).ShouldBe(expected);
    }
}
