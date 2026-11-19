using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace CampusFlow.Application.Tests.BillApprovals;

public class BillApprovalCourseReadinessTests
{
    [Theory]
    [InlineData("pending", true)]
    [InlineData("approved", false)]
    [InlineData("withdrawn", false)]
    [InlineData("different-term", false)]
    [InlineData("different-student", false)]
    public async Task Only_active_selections_with_an_outstanding_decision_block_approval(string scenario, bool expected)
    {
        var student = Guid.NewGuid();
        var review = new CourseReview(Guid.NewGuid(), null, scenario == "different-student" ? Guid.NewGuid() : student,
            "10", scenario == "different-term" ? "268" : "269", "20", "30", "Residential", "{}", null, null, true);
        if (scenario == "approved") review.Approve(Guid.NewGuid(), DateTime.UtcNow, null);
        var repository = Substitute.For<IRepository<CourseReview, Guid>>();
        repository.AnyAsync(Arg.Any<Expression<Func<CourseReview, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Expression<Func<CourseReview, bool>>>().Compile()(review));
        var courses = Substitute.For<IStudentInformationSystemCourseSelectionLookup>();
        courses.GetRegistrationsAsync("10", "269", Arg.Any<CancellationToken>()).Returns(
            new[] { new CourseSelectionRegistration(StudentInformationSystemProvider.ThesisElements, "30", "20", "269", "THE", "101", "", "A", "Course", 3,
                "Unofficial", null, scenario == "withdrawn" ? DateTime.UtcNow : null) });
        Assert.Equal(expected, await new BillApprovalCourseReadiness(repository, courses).HasPendingAsync(student, "10", "269"));
    }
}
