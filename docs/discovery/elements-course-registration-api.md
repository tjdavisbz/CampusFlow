# Elements Course Registration API discovery

Source: the configured Thesis Elements Registration module Swagger document, inspected
read-only on 2026-08-14. No API operation described below was invoked.

The direct Elements gateway Registration Swagger was subsequently discovered and compared
with the Integration API Registration Swagger. The following operation definitions and
their request schemas are identical in both APIs:

- `load-info`;
- `set-semaphore`;
- `student-status/{termID}/{studentUID}`;
- `save-portal-registration`; and
- `cancel/{termID}/{studentUID}`.

CampusFlow therefore uses the Integration API as its default transport because its
tenant-routing and authentication are already configured and supported. The direct Elements
gateway is an equivalent route to the same Registration contract, not a separate business
workflow. It can become a configurable transport option later without changing the
provider-neutral Course Selection domain.

## Confirmed supported operations

### Portal registration save

`POST /api/academic/register/save-portal-registration`

- Explicitly documented as the registration save called from portals.
- Accepts `SaveRegistrationPortalRequest` containing the student, registration mode,
  registration parameters, the complete registered-course collection, total credits,
  student status, and portal type.
- Returns `StringResponse`.
- This is the strongest semantic match for Student Portal Course Selection, but it requires
  a complete state payload. CampusFlow must not synthesize or omit fields without validating
  the service implementation or a known-good request captured from Elements.

### Mass student registration

`POST /api/academic/mass-student-registration/register-student`

- Accepts one or more `RegisterStudentRequest` objects.
- A request identifies the student, term, offered-course IDs, effective date, and validation
  switches for conflicts, maximum courses/hours, prerequisites, equivalents, repeats, and
  corequisites.
- The Swagger request has no registration-status or unofficial flag. It must not be used by
  CampusFlow until Nelson confirms that it creates an **Unofficial** registration in this
  context.

### Mass course drop

`POST /api/academic/mass-student-registration/drop-course`

- Accepts `DropStudentCoursesRequest`: student ID, term ID, offered-course IDs, and effective
  date.
- Returns per-student status/error results.
- This is a plausible supported replacement for the legacy direct `SRAcademic` delete, but
  its drop-versus-delete behavior and effects on billing/history must be confirmed.

### Other endpoints rejected for this workflow

- `DELETE /api/academic/register/cancel/{termID}/{studentUID}` appears to cancel a student's
  entire term registration, not one selected course.
- `POST/DELETE /api/schedule-registration` manages a registration schedule/calendar record;
  it does not represent a student's course enrollment.
- `POST /api/academic/register/load-info` is not read-only. Its Swagger summary states that
  it can create Student Status when missing, so CampusFlow must treat it as a write operation.

## Required confirmation before enabling writes

Obtain either the relevant Elements Integration API implementation code or a sanitized,
known-good request/response from an existing supported client for:

1. adding one offered course as `Unofficial`;
2. obtaining/verifying the resulting `SRAcademicID`;
3. dropping or removing one unofficial course without cancelling the term; and
4. the expected validation flags and failure response semantics.

Until these are confirmed, CampusFlow exposes a provider-neutral course-registration command
boundary but deliberately has no Thesis Elements write implementation. The read-only course,
term, capacity, student-status, and current-registration lookup is implemented separately.

## Legacy Student MRP confirmation

The tenant-supplied `Student_MRP` classic ASP source was inspected as read-only reference.
It does not invoke the modern REST Portal API directly. ASP pages create compiled
`PortalAgent.*` COM objects, so the gateway URL, authentication, and HTTP payload assembly
are hidden inside an unprovided installed component.

The source does confirm the legacy registration transaction:

1. Read the student's current registered courses, waiting list, registration status, and
   available course rows.
2. Determine `RegMode`. It defaults to `Unofficial`; it changes to `Official` only when the
   student already has official courses and tenant portal configuration explicitly permits
   official Student Portal registration.
3. Validate the complete requested change set, including valid offers for the term,
   duplicate sections, availability/waitlist behavior, schedule conflicts, corequisites,
   maximum allowed hours, drop/withdraw dates, and registration-status consistency.
4. Mutate the in-memory complete course collection for adds, audits, drops, withdrawals,
   and waitlist changes.
5. Call `SaveOnLineRegistration` with student, term, the complete course collection, the
   complete student-status record, and `RegMode`.

This strongly supports using Integration API
`POST /api/academic/register/save-portal-registration`, whose request mirrors the same full
state transaction. CampusFlow should use `registrationMode = "Unofficial"` for Nelson's
course-selection workflow and must submit a complete, freshly loaded state rather than a
partial list.
