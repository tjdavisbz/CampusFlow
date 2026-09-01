# Advisor Portal legacy mapping

## Source of truth

CampusFlow owns the new advisor-review workflow. Pending work comes from
`AppCourseReviews`, not from `3DTech_Collegiate_tblEnrollments` or
`SAGU_SchedulerStudentSelection`. Elements remains the source for current student,
term, program, and course-registration facts.

## Identity

An Advisor Portal user signs in through Microsoft Entra ID. CampusFlow links that
identity to an Elements `CAMSUser` without using the legacy Elements password.

Identity resolution order:

1. An explicit tenant-scoped CampusFlow identity link.
2. Elements `CAMSUser.ActiveDirectoryIdentifier` matched to a stable Entra object
   identifier.
3. A unique normalized `CAMSUser.EmailAddress` match, when one exists.

The explicit link is required when the Entra email and Elements email differ. A
missing email match must not prevent an administrator from linking the account.
Disabled Elements users (`DisableLogin = 1`) are not eligible for a new automatic
link.

## Configurable routing facts

The legacy `SAGU_SchedulerStudentSelection` function routes with the following
facts. All rules must move into tenant-owned CampusFlow configuration:

| Routing fact | Elements source |
| --- | --- |
| Attendance type override | `CAMS_StudentStatusUserDefined_View.UDefLookup1Value` |
| Default attendance type | Student attendance type used by the existing course-selection context |
| Student level | `StudentStatus.StudentLevel` for student and term |
| Major | Primary (`PriProgID = 1`) `CAMS_StudentProgram_View.MajorDegree` |
| Specialization/minor | Primary `CAMS_StudentProgram_View.MinorDegree` |
| Degree | Active (`DegreeEarned = 'No'`) `CAMS_StudentDegree_View.Degree` |
| Graduation intent | `3DTech_Registration_tblIntentToGraduate.Intent = 'Yes'` for student and term |

Routing supports include/exclude matching for attendance types, student levels,
major text, degree text, and graduation intent. Rules have an explicit priority and
may assign one or more advisors. A separate tenant permission grants a reviewer the
global queue; it is not represented as a special username list.

## Queue behavior

- A student appears once per term when at least one `CourseReview` still needs
  review and is visible to the signed-in advisor.
- Queue ordering uses the earliest pending review creation time, matching the
  legacy `FirstInsert` intent.
- The student disappears only after every course has reached a final approved,
  rejected, or student-removed state.
- Advisors can submit decisions for only some courses. Unanswered courses remain
  pending.
- Per-course comments are stored on each review. Overall comments create an
  append-only `CourseReviewSubmission`; they are not overwritten.

## Decision behavior

- **Approve:** mark the CampusFlow review approved and clear `NeedsReview`. The
  Elements registration remains in place. CampusFlow does not rewrite
  `SRAcademic.InsertUserID` merely to emulate the legacy portal.
- **Reject:** invoke the supported Elements course-removal API, verify the removal
  through the read-only database, and then finalize the CampusFlow rejection.
- Never call `SAGU_ProccessHoldingTankCourses` or directly delete `SRAcademic` rows.
- Failed external removals remain visible and retryable with their error and attempt
  history preserved.

## Deferred email

The legacy portal sends from the advisor's address after each submission. Email is
out of scope until CampusFlow has an approved mail service. Submission records keep
the information needed to add that delivery later without changing decision history.
