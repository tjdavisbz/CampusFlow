using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace CampusFlow.Application.Tests.BillApprovals;

public class ApprovedCourseSelectionTests
{
    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("graduation")]
    public async Task Approved_bill_rejects_student_mutations_before_any_Elements_call(string action)
    {
        var current=Substitute.For<ICurrentStudentView>();
        var profile=new StudentProfile(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
        current.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        var completion=Substitute.For<IBillApprovalCompletionLookup>();
        completion.IsApprovedAsync(profile,"269",Arg.Any<CancellationToken>()).Returns(true);
        var lookup=Substitute.For<IStudentInformationSystemCourseSelectionLookup>();
        var registration=Substitute.For<IStudentInformationSystemCourseRegistrationService>();
        var manager=Substitute.For<IUnitOfWorkManager>();
        var service=new CourseSelectionAppService(Substitute.For<IRepository<StudentProfile,Guid>>(),current,
            Substitute.For<IRepository<RegistrationTermConfiguration,Guid>>(),Substitute.For<IRepository<AdvisorAssignment,Guid>>(),
            Substitute.For<IRepository<CourseReview,Guid>>(),Substitute.For<IRepository<CourseSelectionOperation,Guid>>(),
            Substitute.For<IRepository<StudentGraduationIntent,Guid>>(),Substitute.For<IRepository<CourseSectionAttendanceTypeMapping,Guid>>(),
            lookup,registration,Array.Empty<IStudentInformationSystemDegreeAuditLookup>(),manager,
            NullLogger<CourseSelectionAppService>.Instance,completion);
        Task Mutate() => action switch
        {
            "add" => service.AddAsync(new(){ExternalTermId="269",ExternalOfferingId="1",IdempotencyKey=Guid.NewGuid().ToString()}),
            "remove" => service.RemoveAsync(new(){ExternalTermId="269",ExternalOfferingId="1",ExternalRegistrationId="2",IdempotencyKey=Guid.NewGuid().ToString()}),
            _ => service.SaveGraduationIntentAsync(new(){ExternalTermId="269",IntendsToGraduate=true})
        };
        await Assert.ThrowsAsync<UserFriendlyException>(Mutate);
        Assert.Empty(registration.ReceivedCalls());Assert.Empty(lookup.ReceivedCalls());
    }
}
