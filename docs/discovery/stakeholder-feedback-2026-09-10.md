# Stakeholder feedback from September 10, 2026

## Source and purpose

This document distills the September 10, 2026 CampusFlow review with representatives
from Academics, Financial Aid, Billing, and IT. The meeting demonstrated the current
Azure-hosted portal and gathered feedback before hands-on testing. Statements below
are categorized so that exploratory discussion is not mistaken for an approved
implementation requirement.

Participants included TJ Davis, Mark Walker, Peggy Jones, Jessica Avery, Chandler
Maynard, NiCole Funchess, Anthony Zoucha, Morgan Stone, and Jeff Francis.

## Confirmed direction

- CampusFlow remains one portal for students, advisors, and administrators. Navigation
  and available pages are determined by the signed-in person's roles and permissions.
- Administrative access should be role based. Reusable department or function roles
  are preferred over assigning individual permissions directly to each person.
- Administrators with the appropriate authority should manage advisor assignments,
  registration windows, attendance mappings, bill agreements, payment plans, and bill
  approval without asking IT to make routine changes.
- Student impersonation is a read-only support tool. The visible impersonation state
  and write protections are essential for security and auditability.
- Students and employees use Microsoft authentication at the same portal entry point.
- CampusFlow may store workflow data that does not belong in Elements, such as a
  student's intent to graduate, while retaining Elements as the source for established
  student and academic records.
- Departmental feedback should be consolidated by functional area and sent through
  Mark rather than arriving as many unrelated requests.

## Product work identified

### Advisor assignments

- Expand assignment rules beyond attendance type. A rule should be able to combine
  attendance type with program, major, degree, or another supported student attribute.
- Preserve support for multiple advisors on one rule and overlapping rules.
- Represent global student visibility within the Advisor Assignments experience when
  practical instead of requiring administrators to understand a separate technical
  permission such as `Advisor Global Reviewer`.
- Retain the ability to map one student attendance type to more than one offered-course
  attendance type.

### Course selection and registration

- Ask whether the student plans to graduate for the selected term. Store the answer in
  CampusFlow and make it available to reporting where needed.
- Continue allowing administrators to configure course-selection windows and rules by
  term.
- Early workflow testing is expected to use Fall 2026 and include at least one
  undergraduate and one graduate student so different registration paths are exercised.

### Student records and privacy

- Add access to an unofficial transcript. This is an existing student-portal function
  and is considered necessary for replacement coverage.
- Add FERPA contact management comparable to the legacy portal: a student can identify
  an authorized person and establish the verification password used when that person
  contacts the university. Portal access for the authorized person was not requested
  for the initial release.
- Perform a specific accessibility review against applicable requirements rather than
  assuming framework defaults are sufficient.

### Identity and access administration

- Remove or reorganize redundant Users and Roles navigation so administrators have one
  clear place to manage identities, roles, and permissions.
- Simplify staff-user provisioning for Microsoft-only authentication. Administrators
  should not be asked to invent a local password that will never be used.
- Separate staff and student identities clearly in administration, and prevent student
  identities from receiving staff or administrative permissions accidentally.
- Support an explicit link between an employee identity and that employee's own student
  record. Removing employee access must not prevent the person from signing in with
  their student identity.
- Duplicate Elements employee records or duplicate email matches must be treated as an
  identity-resolution exception rather than selected arbitrarily.

### Billing and financial aid

- Continue refining the payment-plan editor for usability as Billing tests real plan
  creation.
- Preserve agreement version history and the exact version accepted by each student.
- The meeting discussed whether more than one payment-plan option may eventually be
  offered. This remains a discovery question, not a confirmed requirement.
- Financial Aid currently needs student-facing display but has no identified admin
  configuration requirement. Stable eligibility rules should remain application logic;
  only rules the institution expects to change should become configuration.

## Impersonate Student findings

Two separate defects were reproduced during the meeting.

### Permission behavior

Assigning the `CampusFlow.StudentImpersonation` permission did not make the menu item
appear for other administrators. Access was restored temporarily by adding individual
emails to application configuration. This confirms that the implementation incorrectly
requires both the CampusFlow permission and a configuration allowlist entry. The durable
rule should make the tenant-scoped permission authoritative; any configured email list
should be limited to initial bootstrap provisioning.

### Student lookup performance

Student search intermittently exceeded the SQL command timeout in both Azure and the
local development environment while other Elements-backed pages remained responsive.
The failure occurs in `ThesisElementsStudentLookup.SearchAsync` when executing a single
broad query that combines all of the following across student and address views:

- exact student ID;
- converted Student UID;
- trimmed email address; and
- leading-wildcard searches over concatenated first, preferred, and last names.

The combined `OR` predicates, conversions, trimming, concatenation, `DISTINCT`, and
leading wildcards can prevent useful index access and produce an expensive or unstable
query plan. Starting an impersonation session then repeats the same general search using
the selected external student ID.

The intended correction is:

1. Classify input before querying: numeric ID, email address, or name.
2. Use a narrow exact query for Student ID or Student UID.
3. Use a narrow normalized exact query for email.
4. Run wildcard name matching only for name input and limit results.
5. After selection, load the student through a dedicated exact Student UID lookup rather
   than rerunning the general search.
6. Handle SQL timeout errors with a useful retry message and structured timing logs that
   do not record private search text.

Increasing the command timeout alone is not considered a durable fix.

## Testing and release operations

- Functional teams will compare CampusFlow with the current portal using representative
  students and return consolidated gaps by department.
- A cross-functional walkthrough should exercise the same undergraduate and graduate
  students through Course Selection and Bill Approval.
- Configuration changes restart the App Service and can briefly interrupt active users.
  Planned maintenance or deployment windows should be communicated to the testing group.
- During prelaunch testing, changes may be grouped into a regular release window with
  concise release notes. The production cadence should be reconsidered once live student
  activity begins.

## Related operational item outside CampusFlow scope

The group also discussed ticket 3045 concerning Fall 2026 end-of-cycle student statuses
in Elements, including records that had not moved to a matriculated status. That issue
affects federal reporting and conditional holds but is an Elements operational item, not
a CampusFlow requirement unless later evidence shows the portal must participate.

