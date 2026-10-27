using System;
using System.Linq;
using CampusFlow.Identity;
using Shouldly;
using Volo.Abp.Identity;
using Xunit;

namespace CampusFlow.Identity.Tests;

public class StaffUserDirectoryQueryTests
{
    [Fact]
    public void Only_CamsUser_Emails_Appear_Even_When_A_Staff_Member_Is_Also_Faculty()
    {
        var staffFaculty = User("staff.faculty@nelson.edu", "Staff Faculty");
        var student = User("student@my.nelson.edu", "Student");
        var facultyOnly = User("faculty@nelson.edu", "Faculty");
        var users = new[] { student, facultyOnly, staffFaculty }.AsQueryable();

        var result = StaffUserDirectoryQuery.ApplyFilter(users,
            ["STAFF.FACULTY@NELSON.EDU"], null).ToArray();

        result.Length.ShouldBe(1);
        result[0].Id.ShouldBe(staffFaculty.Id);
    }

    [Fact]
    public void Search_And_Paging_Work_After_Staff_Filtering()
    {
        var alice = User("alice@nelson.edu", "Alice");
        var bob = User("bob@nelson.edu", "Bob");
        var student = User("alex@my.nelson.edu", "Alice Student");
        var filtered = StaffUserDirectoryQuery.ApplyFilter(
            new[] { student, bob, alice }.AsQueryable(),
            ["ALICE@NELSON.EDU", "BOB@NELSON.EDU"], "ali");

        filtered.Count().ShouldBe(1);
        StaffUserDirectoryQuery.ApplySort(filtered, "userName asc")
            .Skip(0).Take(10).Single().Id.ShouldBe(alice.Id);
    }

    private static IdentityUser User(string email, string name)
    {
        var user = new IdentityUser(Guid.NewGuid(), email, email, null) { Name = name };
        user.SetEmailWithoutValidation(email, email.ToUpperInvariant());
        user.SetUserNameWithoutValidation(email, email.ToUpperInvariant());
        return user;
    }
}
