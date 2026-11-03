using System;
using Shouldly;
using Xunit;

namespace CampusFlow.CourseSelections;

public sealed class CourseReviewSubmissionTests
{
    [Fact]
    public void Recording_a_student_note_is_idempotent()
    {
        var submission = new CourseReviewSubmission(Guid.NewGuid(), null, Guid.NewGuid(), "269",
            Guid.NewGuid(), "advisor@nelson.edu", "Please meet with me.", "[]",
            new DateTime(2026, 11, 3, 9, 0, 0), ReviewEmailStatus.Pending);
        var firstWrite = new DateTime(2026, 11, 3, 9, 0, 1);

        submission.RecordStudentNoteWritten(firstWrite);
        submission.RecordStudentNoteWritten(firstWrite.AddMinutes(1));

        submission.StudentNoteWrittenAt.ShouldBe(firstWrite);
    }
}
