using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using NSubstitute;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.RegistrationScopes;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class SchedulerPrimaryProgramAccessTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Queue_direct_review_and_submit_use_only_the_selected_terms_primary_program(bool lookupUnavailable, bool impersonating)
    {
        var tenantId = Guid.NewGuid();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        using var actor = GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AbpClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(AbpClaimTypes.Email, impersonating ? "admin@example.test" : "scheduler@example.test")], "Test")));
        var profile = new StudentProfile(Guid.NewGuid(), tenantId, Guid.NewGuid(),
            new(StudentInformationSystemProvider.ThesisElements, "100", "1000", "student@example.test", "Test", null, "Student"));
        var assignment = new AdvisorAssignment(Guid.NewGuid(), tenantId, "Residential", "1", "scheduler@example.test", "Scheduler", DateTime.UtcNow.AddDays(-1));
        assignment.ConfigurePrimaryProgramScope(new([10], [], []));
        await WithUnitOfWorkAsync(async () => {
            await GetRequiredService<Volo.Abp.Identity.IIdentityUserRepository>().InsertAsync(
                new Volo.Abp.Identity.IdentityUser(profile.UserId!.Value, "test-" + Guid.NewGuid().ToString("N"), "student@example.test", tenantId), true);
            await GetRequiredService<IRepository<StudentProfile,Guid>>().InsertAsync(profile, true);
            await GetRequiredService<IRepository<AdvisorAssignment,Guid>>().InsertAsync(assignment, true);
            foreach (var term in new[] { "268", "269" })
                await GetRequiredService<IRepository<CourseReview,Guid>>().InsertAsync(new(Guid.NewGuid(), tenantId, profile.Id, "100", term, "1", term, "Residential", "null", "1", "scheduler@example.test", true), true);
        });
        var courseLookup = Substitute.For<IStudentInformationSystemCourseSelectionLookup>();
        courseLookup.GetActiveRegistrationIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>{"268","269"});
        courseLookup.GetRegistrationsAsync("100", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => new CourseSelectionRegistration[]{
            new(StudentInformationSystemProvider.ThesisElements, call.ArgAt<string>(1), "1", call.ArgAt<string>(1), "TEST", "101", "", "100", "Example", 3, "Unofficial", null, null) });
        var programs = Substitute.For<IStudentPrimaryProgramLookup>(); programs.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        programs.GetAsync(Arg.Any<IReadOnlyCollection<StudentTermKey>>(), Arg.Any<CancellationToken>()).Returns(call => {
            if (lookupUnavailable) throw new InvalidOperationException("Replica unavailable");
            return call.ArgAt<IReadOnlyCollection<StudentTermKey>>(0).Select(key => new StudentPrimaryProgram(key, key.ExternalTermId == "269" ? 10 : 11, "Primary major")).ToArray();
        });
        var view = Substitute.For<ICurrentSchedulerView>();
        view.IsActive.Returns(impersonating); view.Email.Returns("scheduler@example.test");
        var writer = Substitute.For<IStudentInformationSystemCourseRegistrationService>();
        var service = new AdvisorPortalAppService(GetRequiredService<IRepository<CourseReview,Guid>>(), GetRequiredService<IRepository<CourseReviewSubmission,Guid>>(),
            GetRequiredService<IRepository<StudentProfile,Guid>>(), GetRequiredService<IRepository<AdvisorAssignment,Guid>>(), GetRequiredService<IRepository<StudentGraduationIntent,Guid>>(),
            writer, courseLookup, Substitute.For<IStudentInformationSystemStudentNotesService>(), GetRequiredService<IUnitOfWorkManager>(), [programs], view) { LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>() };
        await WithUnitOfWorkAsync(async () => {
            // Extra-properties scopes survive a real database round trip.
            var stored = await GetRequiredService<IRepository<AdvisorAssignment,Guid>>().GetAsync(assignment.Id);
            Assert.Equal(10, Assert.Single(stored.GetPrimaryProgramScope().MajorIds));
            var queue = await service.GetQueueAsync();
            if (lookupUnavailable) Assert.Empty(queue);
            else
            {
                var student = Assert.Single(queue);
                Assert.Equal("269", student.ExternalTermId);
                Assert.Equal("Primary major", student.PrimaryProgram);
            }
            await Assert.ThrowsAsync<AbpAuthorizationException>(() => service.GetStudentReviewAsync(profile.Id, "268"));
            if (!lookupUnavailable) Assert.Single((await service.GetStudentReviewAsync(profile.Id, "269")).Courses);
        });
        await Assert.ThrowsAsync<AbpAuthorizationException>(() => service.SubmitAsync(new(){StudentProfileId=profile.Id,ExternalTermId="268"}));
        Assert.Empty(writer.ReceivedCalls());
        if (impersonating)
        {
            view.IsActive.Returns(false);
            await WithUnitOfWorkAsync(async () => Assert.False(await service.HasAccessAsync()));
        }
    }
}
