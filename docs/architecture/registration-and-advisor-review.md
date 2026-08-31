# Registration and advisor course review

This document records Nelson's required Registration and Advisor Portal workflow. It is
the durable source of context for future CampusFlow development threads. The legacy
Scheduler Portal code can be supplied separately and should be used to validate field,
procedure, and integration details before implementation.

## Tenant portal boundaries

Each CampusFlow tenant is planned to have three distinct portal experiences backed by a
shared tenant-scoped domain:

- **Student Portal:** student-facing registration, including Meal Plan Selection, Course
  Selection, Bill Approval, and later registration steps.
- **Admin Portal:** configuration and management for the Student and Advisor portals,
  including eligible-term rules, attendance-type mappings, advisor assignments, and other
  tenant-specific workflow policy.
- **Advisor Portal:** advisor course-review queues, decisions, comments, history, and
  follow-up actions. The older Nelson application and some historical documentation call
  this the Scheduler Portal.

Configuration must not be embedded in either portal's page code. Both portals consume the
same published, versioned tenant policy so a policy change is auditable and does not alter
historical review snapshots.

Section-to-attendance-type eligibility is also tenant-owned CampusFlow configuration.
Mappings support overlapping inclusive section ranges, Elements attendance-type identifiers,
effective dates, and activation. Elements remains the source for offered-course facts, but
CampusFlow does not depend on the legacy `SAGU_SectionAttendanceTypes` table at runtime.

## High-level workflow

1. A student selects courses in the CampusFlow Student Portal.
2. The selected courses are written to Thesis Elements as **unofficial courses** and
   immediately appear on the student's Elements schedule. From Elements' perspective the
   registration has occurred; CampusFlow adds a separate institutional review workflow.
3. Each selected course that requires advisor review receives a tenant-scoped review record
   in the CampusFlow PostgreSQL database. The authoritative review flag must not depend on a
   page visit, browser state, or an inferred Elements registration status.
4. A flagged course causes the student to appear in the appropriate advisor's Advisor
   Portal queue.
5. The advisor opens the student and reviews the courses selected for the applicable term.
6. The advisor may act on any subset of courses during one submission:
   - approve one or more courses;
   - reject one or more courses;
   - leave individual courses pending;
   - add a comment to any course without making a final decision; and
   - add an overall comment for the review interaction.
7. Submitting the review emails the student from the advisor's email address. The student
   and advisor may then communicate by email, and the advisor may return later to revise or
   complete the remaining decisions based on the student's response.
8. When every flagged course for that student and term has reached a final approved or
   rejected outcome, the student is removed from the advisor's active queue.

## Decision effects

- **Approved:** retain the unofficial course in Elements and clear its CampusFlow
  `NeedsReview` state. Preserve the decision, advisor identity, timestamp, and comments for
  audit/history; clearing the active flag must not erase the review record.
- **Rejected:** remove the course registration from Elements through a supported write API,
  then mark the CampusFlow review item rejected and no longer needing review. The external
  removal and local status transition must be retry-safe and must not report success until
  the Elements result is verified.
- **Pending/comment only:** keep the course in Elements and keep the CampusFlow review item
  active. A partial submission must not force decisions on other courses.

## Advisor identity and assignment

- Advisors are Elements users represented by `CAMSUser` records.
- Interactive authentication uses Microsoft Entra ID.
- The verified Microsoft email address is matched to the email address on the Elements
  `CAMSUser` record. Authentication alone does not grant access to an advisor queue.
- Advisors are assigned to students according to the student's attendance type.
- Attendance-type-to-advisor assignment must become tenant-admin configuration rather than
  Nelson-specific conditional code.
- An advisor may see only students assigned to them under the configured attendance-type
  rules, unless a future explicit administrative permission grants broader access.

## CampusFlow persistence requirements

Course review state belongs in CampusFlow PostgreSQL even though the underlying unofficial
course resides in Elements. The model must support at least:

- tenant, student profile, Elements student identifier, and term identifiers;
- a stable Elements course-registration/offering identifier;
- the course snapshot shown when the student submitted it;
- active `NeedsReview` state and a status such as Pending, Approved, Rejected, or Failed;
- assigned advisor identity and the matched Elements `CAMSUser` identifier;
- per-course advisor comments;
- overall comments for each submitted review interaction;
- decision/submission timestamps and acting CampusFlow/Entra user;
- Elements removal status, retry count, last attempt, and last error for rejections; and
- audit/concurrency metadata so two sessions cannot silently overwrite each other.

The aggregate should allow partial review submissions. Overall comments are interaction
history, not a single mutable field that loses earlier conversation context.

## Queue semantics

- A student appears in an advisor's active list when at least one course for the selected
  term remains flagged for review.
- The student remains listed after a partial submission.
- The student disappears only when no review item for that student and term remains active.
- Historical completed reviews must remain available to authorized users even after they
  leave the active queue.

## Email boundary

Submission is intended to email the student from the advisor's address and include the
course decisions/comments and overall comment. CampusFlow does not yet have an outbound
email service, so email implementation is deferred until Nelson configures Microsoft Graph,
an approved SMTP relay, or another provider. Review persistence and Elements changes must
not be rolled back merely because email delivery is unavailable or fails. Email delivery
should have its own pending/completed/failed status and retry path.

## Implementation boundary for Registration

The upcoming Registration work must create the CampusFlow review record at the same logical
time that the unofficial course is added to Elements. The workflow must account for partial
failure explicitly: a course must not exist in Elements without a recoverable review flag,
and CampusFlow must not claim the course was added if the Elements write failed. Use stable
idempotency/correlation identifiers where the Elements API permits them.

Before coding the Elements write and removal operations, validate the legacy Scheduler Portal
code and current Elements API for exact identifiers, attendance-type fields, `CAMSUser` email
fields, unofficial registration operations, and course-removal behavior.
