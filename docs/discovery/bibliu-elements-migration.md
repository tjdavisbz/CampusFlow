# BibliU migration: CAMS to Elements

Review date: 2026-10-26. Initial discovery findings with implementation follow-up.
No flow edits, source writes, production configuration changes, or deployment performed.

The requested configuration screen and export API have since been implemented;
see [operating guide](../bibliu.md). The user confirmed preserving existing rules,
using approved CampusFlow bills for eligibility. The design/decision notes below
record the discovery context; they are not outstanding approval requests.
Final file/identifier reconciliation remains a cutover check.

## Evidence and limits

Reviewed the three flows in Safari in the Nelson Production Power Automate
environment, the user-supplied CAMS definition/dependency/column result sets,
and current CampusFlow implementation. All four CAMS root views were found;
database VIEW DEFINITION permission was present. Supplied dependency edges
reported local recorded dependencies only. This does not prove absence of
dynamic/unrecorded dependencies. SQL definition result cells have flattened
line breaks; do not execute those cells as deployment scripts (line comments
can swallow subsequent SQL). Preserve original module scripts for exact review.

Repository readers are implementation evidence, not live confirmation of the
Elements replica's present schema, row semantics, freshness, or identifier continuity.
Live metadata connectivity was verified subsequently; export parity has not been tested.

### Live reporting-replica verification (2026-10-26)

Executed the metadata script through the configured `ThesisElementsReadOnly`
connection against `EL-NEL-DB`; database VIEW DEFINITION permission is available.
No student rows were selected and no source objects/data were changed.

* All 18 required standard objects exist, including DropBag, student holds,
  faculty addresses, award reference rules, billing ledger/batch and term calendar.
* Verified the key legacy fields: EstimatedAmount, award-reference statement/loan
  flags, TotalEnrolled, offering Status, DropBag CollegeID/SROfferID, hold
  category/status, TransDoc and billing void/reversal flags.
* None of the 14 checked legacy custom bookstore/config/approval/balance/padding
  objects exists in the replica. This includes all four export views,
  SAGU_Booktsore_vConfig, SAGU_Bookstore_vBibliuTerms, SAGU_ApprovedBills,
  pending-aid/balance helpers, ledger/batch helper and padding functions.
  Reimplement these in CampusFlow; do not depend on copying them into the replica.
* Fall 2026 is term ID 268 (B26Q); Spring 2027 is 269 (B27C); Summer 2027 is
  270 (B27L). This verifies the selected and expanded terms exist; it does not
  establish continuity of every student/offering ID with BibliU.

Local metadata results are retained in the ignored `tools/local/bibliu-probe/`
directory. No connection string or credential is included in this document.

## Current flow contracts

| Flow | CAMS views | Schedule | SFTP file |
| --- | --- | --- | --- |
| Bookstore User Import File POS Exports | SAGU_Bookstore_vUserImportFile | Hourly, Central | /123/inbox/Bookstore_UserImportFile.txt |
| Bookstore Course Import File POS Exports | SAGU_BookStore_vCourseImportFile | Midnight daily, Central | /campus/CourseImport_123.csv |
| Bookstore Course Schedule and Enrollment | SAGU_Bookstore_vCourseScheduleFile; SAGU_Bookstore_vCourseEnrollmentFile | Midnight daily, Central; parallel branches | /CourseSchedule.csv; /CourseEnrollment.csv |

Flows read rows through a legacy CAMS SQL connection, format them, and upload
through the Bibliu SFTP connection. The user flow combines identity and voucher
records. Do not treat it as a names-only feed. Full formatting expressions,
pagination, retries, and failure behavior require final verification before edits.

## Configuration and term expansion

`dbo.SAGU_Booktsore_vConfig` (legacy spelling) hard-codes Term=Fall 2026,
BillingTerm=Fall 2026, BibliuTerm=Spring 2027, Store Number=123,
InSite Store Number=241. These are values in the supplied definition, not
approved production defaults for the replacement.

Identity rows and the course POS file use the single BibliuTerm. Voucher rows
use BillingTerm. Schedule and enrollment use SAGU_Bookstore_vBibliuTerms:

| Selected season/year | Expanded schedule/enrollment terms |
| --- | --- |
| Spring Y | Fall Y-1, Spring Y, Summer Y |
| Summer Y | Spring Y, Summer Y, Fall Y |
| Fall Y | Summer Y, Fall Y, Spring Y+1 |

Related terms are included only when found in TermCalendar; UNION deduplicates.
The legacy Term and InSite Store Number have no apparent active use in the four
root definitions. Confirm before retiring them. Do not infer dependencies from
commented historical branches.

## Mapping to current implementation

All readers below use the `ThesisElementsReadOnly` SQL Server connection.
That is the Elements reporting replica, not the direct Elements HTTP API.
Its CAMS-named objects must not be confused with the offline legacy server.

| Requirement | Current evidence | Required work |
| --- | --- | --- |
| Term identity and names | ThesisElementsTermLookup reads TermCalendar, including TermCalendarID, Term, TextTerm and dates | Validate selected IDs; use IDs for joins; resolve season/year expansion without relying on display-name arithmetic where possible. Current list is limited to a date window. |
| Student identity/status/email | ThesisElementsStudentLookup reads CAMS_Student_View and CAMS_StudentAddressList_View; ThesisElementsCourseSelectionLookup reads CAMS_StudentStatus_View | Add bulk term-scoped extraction. Preserve StudentID formatting separately from internal StudentUID. Verify Home/Local address rules and deduplication. |
| Offerings/capacity/instructors | ThesisElementsCourseSelectionLookup reads CAMS_SROffer_View and Faculty | Dedicated export query: picker filters DisplayInPortal=1 and SRMasterID IS NOT NULL, unlike the bookstore query. Picker uses CurrentEnroll/TempEnrollment; legacy exports use TotalEnrolled. Verify counts, dates, status and faculty email. |
| Current enrollment/withdrawals | ThesisElementsScheduleLookup reads CAMS_StudentRegisterScheduleDetail_View joined to CAMS_SRAcademic_View | Dedicated bulk query must preserve CollegeID=0, W-grade inactive handling, and rows without schedule meetings. Do not reuse meeting-level results without deduplication. |
| Dropped enrollment | No DropBag reader found in current source | Verify CAMS_DropBag_View or equivalent history on replica; map inactive records and conflicts with active re-enrollments. |
| Billing and previous balances | ThesisElementsBillingLookup reads Billing, BillingBatch, BatchMaster, TermCalendar, Transdoc | Verify legacy sign conventions, report flags, void/reversal handling and previous-term inclusion. Current DTO does not expose TransDoc code needed to identify BOOK usage. |
| Pending aid | ThesisElementsFinancialAidLookup reads CAMS_FinancialAward_View | Existing reader uses Amount. Legacy pending aid uses EstimatedAmount plus CAMS_FinancialAwardRef_View rules for TEG/Guarantee, loans/student acceptance, statement visibility and SentToBilling. Existing portal totals are not proven equivalent. |
| Voc Rehab hold | No matching student-hold reader found | Verify CAMS_StudentHolds_View or equivalent, category/status and multiplicity. |
| Bill approval | CampusFlow BillApproval stores ExternalStudentId, StudentId, ExternalTermId, AcceptedAt and Status | Establish authoritative eligibility rule; don't equate record existence with approval. Include migrated approvals if required. Tenant isolation is mandatory. |

Relevant files: `src/CampusFlow.EntityFrameworkCore/StudentInformationSystems/ThesisElements*.cs`,
`src/CampusFlow.Domain/BillApprovals/BillApproval.cs`,
`src/CampusFlow.Domain/BillApprovals/BillApprovalStatus.cs`,
`src/CampusFlow.Web/Pages/BillApproval.cshtml.cs`, and
`src/CampusFlow.Web/Pages/Admin/RegistrationRules.cshtml.cs`.

## Rules requiring explicit reconciliation

* Legacy voucher branches INNER JOIN SAGU_ApprovedBills but never test Approved.
  Their AB-is-null fallback is unreachable for joined rows. CampusFlow creates
  a draft before acceptance; existence-only matching would admit drafts.
  AcceptedAt/Status semantics must be agreed before calculating eligibility.
* Voucher limit is generally max(500, absolute credit balance when balance < -500),
  minus positive BOOK usage, floored at zero. Hold changes event code; it does
  not obviously change the amount branch. Reconcile exact cents encoding and
  rounding against expected files before implementing financial output.
* Legacy BOOK aggregation covers ledger and batch and does not visibly filter
  voided/reversing transactions in that aggregation. Do not silently correct
  or reproduce it without reconciling actual intended usage.
* User name rows require Home addresses even though they export names; enrollment
  requires Local email/address. Decide exclusions and fallback policy.
* Enrollment exports both academic W grades and DropBag rows as inactive.
  UNION removes only identical rows, not conflicting statuses for one enrollment.
* Both schedule and enrollment replace 12/09/2025 with 12/12/2025. Treat this as
  a historical exception pending confirmation, not a general date rule.
* POS course IDs/CRNs originate in SROfferID; user identifiers combine store
  number and padded StudentID. Verify continuity against BibliU, not just types.

## Proposed implementation

1. Add a tenant-scoped, audited BibliU configuration entity and admin permission.
   Store separate Elements billing/BibliU term IDs and store identifiers as strings.
   Display expanded terms, validation, concurrency handling and change history.
   Keep registration/bill-approval configuration independent of bookstore terms.
2. Add dedicated, parameterized, read-only bulk export readers against the
   Elements replica. Do not create objects or write config in that replica.
   Read approval eligibility from CampusFlow only after its rule is confirmed.
3. Add a narrowly authorized service endpoint/job that assembles export snapshots
   from CampusFlow config plus Elements data. Prefer Entra application identity
   if compatible with current host authentication; validate issuer, audience,
   tenant and permission. Do not assume existing interactive login suffices.
4. Prefer a shared snapshot/run ID for paired schedule/enrollment outputs, with
   one config version and explicit source freshness. Return the existing row
   contracts initially so Power Automate can retain formatting and SFTP delivery.
   Choose pagination/snapshot storage based on measured row counts. Never
   silently truncate or convert source/config failure into an empty success.
5. Update existing flows to retrieve the approved export contract, removing CAMS
   reads. Check HTTP/custom-connector licensing, tenant DLP, endpoint reachability,
   timeout, retry/idempotency and secure input/output logging before cutover.
   A settings-only API is a smaller alternative if flows retain all extraction,
   but approval/data assembly across databases makes an export service preferable.

Local/staging must have separate config, credentials, endpoints and destination
paths. Preview must not upload to BibliU. Production requires an explicitly
authorized schema migration, deployment and flow cutover; none is authorized by
this discovery. No direct Power Automate PostgreSQL access is needed by this design.

## Validation and next inputs

`bibliu-elements-schema-discovery.sql` has now been run on the Elements reporting
replica. Standard source object availability is confirmed above. Original
line-preserving legacy definitions remain preferable where comment boundaries
need exact verification. Metadata alone cannot prove ID continuity or source behavior.

Then reconcile approved sample exports and source data in a non-production
environment. Tests must cover term expansion/year boundaries, draft vs accepted
approvals, financial thresholds/rounding/usage/holds, drops/re-enrollments,
duplicate addresses/faculty, missing values, fixed-width bytes, CSV escaping,
pagination, config edits mid-run, authorization/tenant isolation and source failures.
Compare counts, business keys, statuses and exact output bytes; keep delivery
disabled until approved. Retain old flows disabled for rollback and avoid
overlapping writers during cutover.

Decision inputs still needed: approval authority/eligible state and historical
coverage; whether legacy voucher and term-expansion rules remain; address fallback;
historical date exception; BibliU ID continuity/expected files; staging destination.
Existing tests cover general term configuration and course policies, but no
BibliU-specific contract/calculation tests were found.
