using System;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Shouldly;
using Xunit;

namespace CampusFlow.Students;

public class StaffStudentLinkTests
{
    [Fact]
    public void Unclaimed_student_profile_can_be_claimed_by_its_student_login()
    {
        var student = new StudentInformationSystemStudent(
            StudentInformationSystemProvider.ThesisElements, "42", "20000042",
            "student@my.nelson.edu", "Test", null, "Student");
        var profile = new StudentProfile(Guid.NewGuid(), Guid.NewGuid(), null, student);
        var studentUserId = Guid.NewGuid();

        profile.AssignUser(studentUserId);

        profile.UserId.ShouldBe(studentUserId);
        Should.Throw<InvalidOperationException>(() => profile.AssignUser(Guid.NewGuid()));
    }

    [Fact]
    public void Staff_link_can_change_student_without_changing_the_student_login()
    {
        var staffUserId = Guid.NewGuid();
        var firstStudentId = Guid.NewGuid();
        var nextStudentId = Guid.NewGuid();
        var link = new StaffStudentLink(Guid.NewGuid(), Guid.NewGuid(), staffUserId, firstStudentId);

        link.ChangeStudent(nextStudentId);

        link.StaffUserId.ShouldBe(staffUserId);
        link.StudentProfileId.ShouldBe(nextStudentId);
    }
}
