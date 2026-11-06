using System;
using System.Collections.Generic;
using System.Linq.Expressions;
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
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.Application.Tests.AdvisorPortal;
public class SchedulerViewAccessTests
{
    private readonly ICurrentSchedulerView _view = Substitute.For<ICurrentSchedulerView>();
    private readonly IRepository<AdvisorAssignment,Guid> _assignments = Substitute.For<IRepository<AdvisorAssignment,Guid>>();
    private readonly IStudentInformationSystemCourseRegistrationService _registration = Substitute.For<IStudentInformationSystemCourseRegistrationService>();
    private readonly IStudentInformationSystemStudentNotesService _notes = Substitute.For<IStudentInformationSystemStudentNotesService>();
    private readonly AdvisorPortalAppService _service;
    public SchedulerViewAccessTests()
    {
        var user = Substitute.For<ICurrentUser>();user.Email.Returns("admin@example.test");user.Id.Returns(Guid.NewGuid());
        var clock = Substitute.For<IClock>();clock.Now.Returns(DateTime.UtcNow);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);lazy.LazyGetRequiredService<IClock>().Returns(clock);
        _view.IsActive.Returns(true);_view.Email.Returns("scheduler@example.test");
        _assignments.GetListAsync(Arg.Any<Expression<Func<AdvisorAssignment,bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AdvisorAssignment>{new(Guid.NewGuid(),null,"Residential","1","scheduler@example.test","Scheduler",DateTime.UtcNow.AddDays(-1))});
        _service = new(Substitute.For<IRepository<CourseReview,Guid>>(), Substitute.For<IRepository<CourseReviewSubmission,Guid>>(),
            Substitute.For<IRepository<StudentProfile,Guid>>(), _assignments, Substitute.For<IRepository<StudentGraduationIntent,Guid>>(),
            _registration, Substitute.For<IStudentInformationSystemCourseSelectionLookup>(), _notes, Substitute.For<IUnitOfWorkManager>(), _view){LazyServiceProvider=lazy};
    }
    [Fact]
    public async Task Assignment_access_uses_the_viewed_scheduler_and_restores_the_actor_on_exit()
    {
        Assert.True(await _service.HasAccessAsync());
        _view.IsActive.Returns(false);
        Assert.False(await _service.HasAccessAsync());
    }
    [Theory]
    [InlineData("submit")]
    [InlineData("note")]
    [InlineData("mail")]
    public async Task Service_entry_points_reject_writes_or_mail_even_without_HTTP_middleware(string action)
    {
        Task Run() => action switch { "submit" => _service.SubmitAsync(new()), "note" => _service.EnsureStudentNoteAsync(Guid.NewGuid()), _ => _service.GetSubmissionEmailAsync(Guid.NewGuid()) };
        await Assert.ThrowsAsync<AbpAuthorizationException>(Run);
        Assert.Empty(_registration.ReceivedCalls());Assert.Empty(_notes.ReceivedCalls());
    }
}
