using System;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.CourseSelections;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public sealed class AdvisorMailStatusPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Email_status_can_be_saved_after_the_note_is_committed_in_another_unit_of_work()
    {
        var profiles = GetRequiredService<IRepository<StudentProfile, Guid>>();
        var submissions = GetRequiredService<IRepository<CourseReviewSubmission, Guid>>();
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var profileId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            await profiles.InsertAsync(new StudentProfile(profileId, null, null,
                new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements,
                    "987654", "test-987654", "test@example.edu", "Test", null, "Student")), autoSave: true);
            await submissions.InsertAsync(new CourseReviewSubmission(submissionId, null, profileId,
                "269", Guid.NewGuid(), "advisor@example.edu", "Please meet with me.", "[]",
                DateTime.UtcNow, ReviewEmailStatus.Pending), autoSave: true);
        });

        using (var requestUnitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            // The note-writing step can leave an older copy tracked by the page request.
            var stale = await submissions.GetAsync(submissionId);

            using (var noteUnitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
            {
                var noteSubmission = await submissions.GetAsync(submissionId);
                noteSubmission.RecordStudentNoteWritten(DateTime.UtcNow);
                await submissions.UpdateAsync(noteSubmission, autoSave: true);
                await noteUnitOfWork.CompleteAsync();
            }

            stale.StudentNoteWrittenAt.ShouldBeNull();

            // Record the accepted Graph response using a fresh concurrency stamp.
            using (var mailUnitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
            {
                var mailSubmission = await submissions.GetAsync(submissionId);
                mailSubmission.StudentNoteWrittenAt.ShouldNotBeNull();
                mailSubmission.RecordEmailResult(DateTime.UtcNow, accepted: true, error: null);
                await submissions.UpdateAsync(mailSubmission, autoSave: true);
                await mailUnitOfWork.CompleteAsync();
            }

            await requestUnitOfWork.CompleteAsync();
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var stored = await submissions.GetAsync(submissionId);
            stored.StudentNoteWrittenAt.ShouldNotBeNull();
            stored.EmailStatus.ShouldBe(ReviewEmailStatus.Sent);
            stored.EmailAttemptCount.ShouldBe(1);
        });
    }
}
