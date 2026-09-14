# Fall 2026 legacy housing migration preview

Initial read-only investigation on 2026-09-13; see execution update below for subsequent
authorized layout initialization and the rejected first assignment. Source is the Elements SQL read-only replica's
legacy dbo.Housing_Assignment rows for Fall 2026, not the Excel workbook or a fresh CAMS export.
Replica freshness and authoritative source selection must be confirmed before execution.

## Counts

780 legacy assignments / 780 distinct students / 418 legacy rooms; no duplicate students.

| Category | Assignments | Finding |
| --- | ---: | --- |
| Unique proposed property + room matches | 596 | Includes 4 occupants with capacity conflicts |
| Commuter | 106 | Exclude from physical room migration; housing status is separate |
| Regents Apartments | 42 | Unit/bedroom mapping decision required |
| Alta M. Washburn Building | 17 | No corresponding master property found |
| Theodore Gannon Dormitory | 19 | No corresponding master property found |
| Total | 780 | |

592 assignments have unique proposed matches and no detected capacity conflict. This is
mapping readiness only, not authorization or proof of API/billing readiness.

## Proposed crosswalk (not approved)

| Legacy building IDs | Elements property ID | Elements name | Assignments |
| --- | --- | --- | ---: |
| 27, 26 | 1002 | Bridges Hall | 161 |
| 3 | 1006 | Collins Hall | 100 |
| 7, 8 | 1007 | Guynes Hall | 121 |
| 10 | 1005 | Kendrick Hall | 22 |
| 42, 18 | 1004 | Savell Hall | 50 |
| 25, 24 | 1003 | Teeter Hall | 142 |

Matched trimmed legacy Room.Number to active master RoomName within the proposed property.
No multiple candidate room matches or merged legacy-room collisions were detected for these
596 assignments. These mappings merge legacy gender/wing building variants; matching labels
alone does not verify gender, campus, room eligibility, or rates. Property CampusID values
were null. Explicitly exclude the active property named `Bridges Hall - Delete` (ID 1).

Teeter rooms 106 and 111 each have capacity 1 in Elements and 2 proposed occupants.
Do not silently increase capacity; confirm intended configuration first.

Regents property 1010 uses legacy apartment numbers as Units.UnitName, with individual
Rooms.RoomName 1 and 2, each capacity 1. Most occupied legacy apartment labels appear as units,
but legacy apartment 311 was not found (312/313 exist). Do not substitute another unit or
arbitrarily assign bedroom 1/2. The 42 assignments remain on hold.

## API checks and unresolved prerequisites

- Integration login and housing-period/property searches succeed.
- Documented POST `housing-periods/room-assignments/search`, with housingPeriodID 2 and
  roomAssigned true, succeeds and returns zero entries. This searches application-based
  assignment records; it does not prove a complete occupant enumeration for a populated period.
- Target `Fall 2026 Prod` (ID 2) has no period properties/rooms; replica has no nondeleted
  housing application submissions for this period.
- AssignRoomRequest includes studentUIds, housingPeriodRoomId, housingPeriodId, and a nonnullable
  integer housingApplicationFormId. Swagger does not establish whether zero is accepted or
  whether an approved application is mandatory. Do not invent an application ID.
- Initialization takes housingPeriodID and integer propertyInitializationType. The meaning of
  the initialization enum must be established from UI/vendor documentation before use.
- Swagger provides separate room rates and charge-management operations but no explicit
  guarantee that assignment is charge-free. Automatic billing behavior is UNVERIFIED.
- Direct gateway still rejected the integration-issued token; this is not a proven user
  permissions failure. Integration has viable documented routes, including assign/deallocate.

## Execution gate

Before writes: approve source/crosswalk and exclusions; resolve application requirements and
initialization mode; establish billing behavior from vendor documentation or a controlled test
approved by the user; snapshot the live target; then test one approved student with before/after
assignment AND charge verification. Do not bulk migrate or reproduce the legacy wipe/rebuild.

Deferred decisions: AIC properties (36 students), Regents bedrooms/missing unit (42 students),
Teeter capacity (4 students), commuter status representation (106 students). No student names,
IDs, credentials, or tokens are included in this document.

## Execution update — 2026-09-13

User approved targeting the 592 eligible assignments. Prepared the immutable manifest
`SAGU SSRS Project/Migration/Housing/fall-2026-first-batch-20260913-210215.json`
(ignored by Git, owner-only file permissions). Assignment SHA256:
`094619778C1F2FBC88FE34DCE212BCE7A055C7223DE93695D53F9D2EB3E399E6`.

Through the authenticated Elements UI, initialized `Fall 2026 Prod` with **Property Layout**.
Vendor help says this copies active master properties into the housing period. It copied all
8 active properties and 499 rooms, including Regents and the master `Bridges Hall - Delete`.
Those two properties remain excluded from student migration; no property was renamed/deleted.
No master capacities, dates, applications, or charge settings were edited.

Verified initialized layout and charge configuration through integration API reads; snapshot:
`SAGU SSRS Project/Migration/Housing/target-layout-20260913-210933.json`.
All copied room rates are zero; property rent/deposit configuration IDs and term are unset.

Opened the manual Assign Room workflow for the first approved student and selected the matched
Bridges Hall room 101. Elements rejected Assign Room with:

> No rent data available for this room. Please set up the rent fee on the corresponding Housing Period configuration before proceeding

No further assignment attempts were made. Per-student assigned-room API returned zero records.
Before/after replica billing snapshots both contained 98 transactions with identical SHA256
`97714CC1B5250C6453306C7C3663692835D505126954302078CEC98C96568569`.
This is immediate replica evidence, not a guarantee against delayed processing; recheck before resuming.

Configured Bridges Hall only for a controlled zero-rent test using one-time payment frequency,
the existing `HOUSING` transaction document, Rental Income category, and an August 21, 2026
due date. Elements saved `HousingPeriodRents.Rent` as `Room Setup`; room 101 remains active,
available, capacity 2, with room and per-person rates of 0.00. No other property was configured.

The manual UI subsequently returned no rooms for this student/property/period even though the
replica contains 118 active Bridges Hall period rooms. Static inspection of the vendor UI confirmed
the exact assignment payload and endpoint. Initial attempts incorrectly sent the housing request to
the registration gateway and returned HTTP 403. Omitting `TenantHost` did not resolve that error.
The housing route belongs on the configured integration `BaseUrl`, matching the successful housing
read calls—not `RegistrationBaseUrl`.

The corrected integration-API call returned HTTP 200 with `isSuccess=true`. The canary student is
now assigned to Fall 2026 Prod / Bridges Hall / room 101. Elements also created one new BillingBatch
row for term 268 with 0.00 debit and 0.00 credit. Billing count changed from 98 to 99; the new
post-assignment snapshot SHA256 is
`2435897ADCFED737C8CD584FA4ACEBA696D9AD920A45C9F8E8D9FEC19C5BDCBB`.

The remaining five approved dorms (Collins, Guynes, Kendrick, Savell, and Teeter) were subsequently
configured through the authenticated Elements UI with the same zero-rent settings. An integration
API readback verified all six dorm configurations. Regents and `Bridges Hall - Delete` remained
untouched. The post-configuration layout snapshot is
`SAGU SSRS Project/Migration/Housing/target-layout-20260913-232945.json`.

A two-record checkpoint then recognized the canary as already assigned and successfully assigned
and verified the next student. The full resumable batch completed at 2026-09-13 23:50 UTC:

- Manifest rows processed: 592
- Newly assigned during the full run: 590
- Already assigned checkpoint rows: 2
- Failed assignments: 0
- Immediate per-student API readback failures: 0

The owner-only audit journal and summary are:

- `SAGU SSRS Project/Migration/Housing/fall-2026-assignment-run-20260913-233256.jsonl`
- `SAGU SSRS Project/Migration/Housing/fall-2026-assignment-run-20260913-233256-summary.json`

**Current status: all 592 approved Fall 2026 assignments are present and passed immediate API
readback.** Still deferred are AIC property creation/mapping, Regents unit/bedroom mapping, the four
Teeter capacity-conflict assignments, commuter status handling, static housing-charge posting, and
a controlled deallocation test to document zero-dollar batch-row cleanup behavior.

## AIC execution update — 2026-09-13

Created two active master residence-hall properties from the legacy Elements replica and verified
their rooms and capacities through direct Elements API readback:

- Theodore Gannon Dormitory: master property 2002, 20 rooms
- Alta M. Washburn Building: master property 2003, 21 rooms

The two active Washburn library rooms on floor 0 were deliberately excluded. Elements' official
reinitialization conflict check returned no conflicts before the Fall 2026 property layout was
reinitialized. The reinitialization added both AIC properties to the period. It also generated new
period-property and period-room identifiers for the existing layout while existing student
assignments retained their earlier identifiers. Consequently, assignment verification now compares
the durable period, property name, and room name instead of internal period-room identifiers.

All 592 previously migrated assignments were reverified after reinitialization. The main pass found
589 correct assignments and three transient network-read failures; targeted retries verified the
remaining three. No student assignment was rewritten during that verification.

Both AIC period properties were configured and read back with the same zero-rent setup used for the
six existing dorms: Room Setup, one-time frequency, HOUSING transaction document 137, Rental Income
category 7, August 21, 2026 due date, no proration, and zero room/per-person rates.

A 36-row AIC manifest was built from legacy Fall 2026 assignments. It contained 36 distinct students,
exact property/room matches, and no capacity conflicts. A single canary assignment passed room
readback and generated a new 0.00 debit / 0.00 credit HOUSING batch row; its unrelated pre-existing
charge was unchanged. The remaining batch then completed with 35 newly assigned, one canary already
assigned, and zero failures. A final replica audit found exactly one zero-dollar HOUSING row for each
of the 36 AIC students, totaling 0.00 debit and 0.00 credit.

Owner-only audit artifacts include:

- `SAGU SSRS Project/Migration/Housing/aic-property-run-20260914-001956.jsonl`
- `SAGU SSRS Project/Migration/Housing/aic-charge-run-20260914-005659.jsonl`
- `SAGU SSRS Project/Migration/Housing/fall-2026-aic-batch-20260914-010034.json`
- `SAGU SSRS Project/Migration/Housing/fall-2026-aic-assignment-run-20260914-010034.jsonl`

The migrated Fall 2026 physical-room total is now **628 assignments**: 592 previously approved plus
36 AIC assignments. Still deferred are Regents unit/bedroom mapping (42), the four Teeter capacity
conflicts, commuter status handling (106), static housing-charge posting, and a controlled
deallocation/zero-dollar cleanup test.

## Regents execution update — 2026-09-13

The legacy source contains 42 Fall 2026 Regents assignments across 23 apartment numbers but does
not identify bedrooms. The housing workbook likewise records only the apartment number. The
approved deterministic conversion maps occupants in StudentUID order to bedroom 1 and then
bedroom 2 within each apartment.

Regents was configured for Fall 2026 with the verified zero-rent setup. Forty students whose
apartments already existed in the initialized period were assigned and immediately read back with
zero failures. A final replica audit found one Elements-created `Rent Per Person` row for every
migrated student, with total debits and credits both 0.00. Pre-existing legacy housing charges were
not modified.

Apartment 311 was absent from the initialized period. It was added to the master Regents property
as a two-bedroom, one-person-per-bedroom unit, but its two legacy occupants remain deferred because
copying that new master unit into Fall 2026 would require a broad period reinitialization that can
regenerate identifiers for the other 628 assignments. No reinitialization was performed.

Owner-only audit artifacts include:

- `SAGU SSRS Project/Migration/Housing/regents-unit311-run-20260914-011106.json`
- `SAGU SSRS Project/Migration/Housing/regents-charge-run-20260914-011202.json`
- `SAGU SSRS Project/Migration/Housing/fall-2026-regents-safe-batch-20260914-011642.json`
- `SAGU SSRS Project/Migration/Housing/fall-2026-regents-assignment-run-20260914-011642.jsonl`
- `SAGU SSRS Project/Migration/Housing/regents-final-verification-20260914-011745.json`

The migrated Fall 2026 physical-room total is now **668 assignments**: 592 approved residence-hall
assignments, 36 AIC assignments, and 40 Regents assignments. Still deferred are the two Regents
apartment-311 occupants, the four Teeter capacity conflicts, commuter status handling (106), static
housing-charge posting, and a controlled deallocation/zero-dollar cleanup test.

## Teeter capacity-conflict update — 2026-09-13

Legacy Teeter rooms 106 and 111 are both active double-occupancy rooms with two Fall 2026
occupants. In the Elements master and initialized Fall 2026 layout, these were the only two rooms in
their sequence configured for one occupant; adjacent rooms use capacity two. The Fall 2026
period-room capacities were corrected from one to two through the narrow period-property save API
and verified by readback. No period reinitialization was performed.

All four previously held students were assigned to their intended Teeter rooms and passed immediate
assignment readback with zero failures. The replica shows no newly inserted billing rows for these
four assignments, and their four pre-existing legacy housing charges are unchanged. This differs
from some earlier assignments, which generated an explicit zero-dollar batch row, but confirms the
Teeter operation introduced no new charge.

Owner-only audit artifacts include:

- `SAGU SSRS Project/Migration/Housing/teeter-capacity-run-20260914-013525.json`
- `SAGU SSRS Project/Migration/Housing/fall-2026-teeter-conflict-run-20260914-013525.jsonl`
- `SAGU SSRS Project/Migration/Housing/teeter-final-verification-20260914-013549.json`

The migrated Fall 2026 physical-room total is now **672 assignments**. Still deferred are the two
Regents apartment-311 occupants, commuter status handling (106), static housing-charge posting,
and a controlled deallocation/zero-dollar cleanup test. The Teeter master layout still reports
capacity one for rooms 106 and 111; its update route is unavailable through the integration API, so
correcting the master layout remains a separate Elements UI maintenance item.

Vendor references inspected in Elements Online Help:
- `Content/02_Reference/Student-Life/Add-Update-Housing-Periods.htm`
- `Content/02_Reference/Student-Life/Assign-Room.htm`
under `https://el-nel.thesiscloud.com/SM%20Lite%20Built%20Online%20Help/`.
