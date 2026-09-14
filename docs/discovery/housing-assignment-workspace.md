# Housing assignment workspace

Feature branch: `codex/housing-assignment-workspace`.

## Scope and implementation status

First increment: Admin → Housing → Assignments; tenant-scoped saved draft per housing period;
inline capacity, intentional-single, rate-choice and note editing; student search and vacant bed
assignment; selected-row clearing; local filtering; refresh confirmation; saved-draft change preview.
Optimistic concurrency prevents an old browser draft overwriting a newer saved revision.

This is NOT yet a live sync implementation. No Elements mutations are exposed. An authenticated
API read probe returned HTTP 403 on `housing-periods/search`; authentication itself succeeded.
The implemented reader instead uses parameterized SELECTs against the Elements read-only replica,
with column names verified against INFORMATION_SCHEMA on 2026-09-13. Both queries executed successfully.
The replica currently contains one non-deleted period: ID 2, `Fall 2026 Prod`, with zero non-deleted
period properties, period rooms, or period assignments. This may reflect incomplete setup or replica
state; do not claim the live Elements environment has no rooms without checking it. Refresh safely
rejects an empty catalogue rather than replacing an existing draft with an empty one.

Generated migration: `20260913202932_AddHousingAssignmentDraft`; not applied to any database.
Apply it locally before using saved drafts; do not promote to production yet.

Verification: web project build succeeded; 8 planner tests passed; isolated headless Chrome test
passed period selection, mock refresh, assignment, inline editing, save, preview and bulk clearing.
That browser test used mock data, not the signed-in CampusFlow application. Live populated refresh,
database save/concurrency, permission grants, and the complete themed screen still need integration testing.
An existing Scriban 7.2.1 dependency advisory (NU1903) appeared while restoring the local probe;
the feature adds no package dependencies. Review that separately before release.

Refresh replaces assignments and all draft-only notes/rate selections with Elements data, with
explicit confirmation. Current increment has no draft-history restore UI. Preferences must be a
separate future entity so refresh cannot discard them. No Excel import has been implemented yet.

## Authoritative references

- User workbook: `HousingAssignments.xlsx`, Assignments and Lookup sheets.
- Student Life / Meal Plans meeting transcript supplied 2026-09-12.
- Live ReportServer export: `ReportServer-Export/Content/Housing/Process - Import Housing From Excel.rdl`.
- Current procedures supplied 2026-09-13, saved under ignored `SAGU SSRS Project/Migration/`:
  `SAGU_Housing_ImportFromExcel-2026-09-13.sql` and
  `SAGU_Housing_ExcelImport_KillItWithFire-2026-09-13.sql`.
- `John Old SQL/Housing` copies are older and must not override current definitions.

## Confirmed legacy rules

- The procedure now lives in `Nelson_ReportingAndIntegration`, using CAMS tables across databases.
- Workbook fields include term, student ID, building, room, intentional single, custom fee, notes,
  and date entered. Blank student slots are vacancies; Demo rows are spreadsheet import sentinels.
- Custom fee is matched by fee name and term against `CAMS_Housing_Fees_View` and overrides the
  standard room fee. Current workbook choices are AIC Triple Room and Savell Triple Occupancy.
  These are configured fee amounts, not a calculated occupancy split.
- Commuter uses room `000` in the legacy system and is excluded from housing charges. Determine
  its Elements representation before implementing workbook import; do not invent a physical room.
- Intentional single is explicitly marked, never inferred from having only one occupant.
  Current surcharge is 85% of base Housing / AIC HOUSING charge; Theodore Gannon Dormitory and
  Alta M. Washburn Building subtract 1,800 before multiplying by 85%. This differs from the old
  archived 50% rule. Do not hard-code either policy permanently; use term-specific configuration.
- Cleanup is term-scoped (optional student scope): deletes Housing Charges batch entries, and
  `SAGU_Housing`-tagged student fees and assignments; fee schedules are deleted through those
  tagged student assignments. Posted ledger entries are not deleted. Batch deletion itself is
  not restricted by InsertUserID. The rebuild checks ledger and other batch entries for duplicates.
- The legacy procedure chooses TOP 1 term from the spreadsheet. CampusFlow must instead require
  an explicit complete housing-period scope; never infer deletion scope from filtered table rows.

## Required before enabling sync

1. Initialize/confirm housing-period properties and rooms in Elements and their presence in the replica.
   Verify complete authoritative room/occupant API reads before writes (see API findings below).
2. Verify assignment API prerequisites (housing application form/status) for existing and new students.
3. Map term-specific rates, charges, and commuter handling to Elements; confirm whether assignment
   APIs automatically generate charges. Avoid both automatic and explicit duplicate billing.
4. Snapshot current Elements state immediately before sync, compare against the refresh baseline,
   and stop on external changes. The read-only SQL replica can lag; final checks should use API reads.
5. Persist operation journal and before/after values, idempotency/retry state, and per-operation results.
   Moves require safe deallocation/assignment recovery; multi-call API work is not one DB transaction.
6. Review all adds/moves/removals, room-capacity changes and actual monetary deltas, then require
   confirmation. Capacity changes should affect only the selected housing period unless explicitly chosen.
7. Never delete posted ledger charges or silently modify unrelated manual batch transactions.
8. Validate refresh/save concurrency, permissions, full-period vs filtered views, and live UI before release.

## Later phases

Student preference windows (same room, same dorm, then different dorm), dorm-pastor/faculty scope,
returning-vs-new student workflows, roommate information, bulk assignment/move operations, Excel
import/export, and draft history. Preferences are requests, not approved assignments.

## API access findings — 2026-09-13

- Elements user administration shows `elements_api` with Read/Write access to Student Life,
  Housing, Housing Periods, manual room assignment, Assign Rooms, and Properties. No permissions
  were changed during inspection.
- A token issued by the configured integration login receives HTTP 403 from the direct Elements
  gateway housing-period search. This does not establish that housing permissions are missing;
  token compatibility with the direct gateway remains unverified.
- The same token succeeds against the integration API for housing-period search and period-property
  search. The active `Fall 2026 Prod` period (ID 2) currently has zero period properties.
- Direct and integration Swagger routes are not identical. The direct `room-assignments/assigned-rooms`
  route is absent from integration Swagger and returns 404 there. Integration documents
  `assign-room/get-assigned-rooms/{StudentUID}` and room-assignment search routes instead.
- Integration Swagger includes property initialization and room assign/deallocate operations.
  Their presence is not proof that a write succeeds; no live writes or migration were attempted.
- Migration needs a reviewed legacy-to-new property/room crosswalk, explicit source term and target
  housing period, duplicate-student resolution, and verified billing side effects before execution.
