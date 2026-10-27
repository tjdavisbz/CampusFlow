# Legacy bill approval import

## Scope and current state

Prepared locally for the nelson production tenant (`3a236d25-0541-8fa3-25ec-fecc83432a1a`). No production schema, approval data, web deployment, or Power Automate flow has been changed by this import preparation. Commit/deployment and production migration/import approval are the remaining operational step.

The supplied tab-separated export is from custom legacy `SAGU_ApprovedBills`, not the Elements reporting replica. Its header is `StudentUID`, `TermCalendarID`, `Approved`, `LoginTime`, `AcceptTime`. Source SHA-256: `1bd80658c5a09a3501b213356365e76eb476f5a8ebf9839a3860d6cc7b6d5f4f`. The source file and student mappings remain private; neither is committed.

## Read-only preflight

| Check | Result |
| --- | ---: |
| Source rows | 83,167 |
| Distinct students | 17,154 |
| Distinct term IDs | 64 |
| Approved flag = 1 | 83,141 |
| Approved flag = 0 | 26 |
| Duplicate student/term pairs | 0 |
| Unmatched Elements students / terms | 0 / 0 |
| Overlap with live CampusFlow approval records | 0 |
| Records that would be inserted | 83,167 |

Full-history student and term mappings were read through `ThesisElementsReadOnly` from the Elements reporting replica, using all source IDs rather than the normal term picker's two-year window. All mapped. Nonzero term names span Spring 2005 through Fall 2026. The table supplies one current legacy record per student/term, not every historical approval/reversal event.

Exceptions are retained, not rewritten:

* Seven approved records in Fall 2024 lack `AcceptTime`. History shows the original approved flag and missing date; they do not count as completed approvals or qualify for BibliU until reviewed.
* One of the 26 unapproved records has an acceptance timestamp. Its unapproved status is retained and it does not qualify.
* Six approved records use term ID `0`, which exists in Elements with code `A` and no display name. History labels it “Unspecified legacy term”; these records do not qualify for academic approval completion or BibliU.
* Original timestamps have no offset. They are stored as `timestamp without time zone` and displayed as recorded. Sentinel login dates such as 1900-01-01 are retained; no approval dates, offsets, signatures, agreements or financial snapshots are invented.

Fall 2026 (268/B26Q) has 1,869 approved records with dates. A read-only export-reader preview produced 6,838 identity rows plus 3,736 voucher rows for 1,868 students (10,574 rows total). One approved student has no Fall 2026 row in `CAMS_StudentStatus_View`; this is an existing export filter. All 1,869 pass the Home-address and numeric-ID filters. This preview did not import approval records or deliver files. After deployment/import, rerun the manual user test flow and verify its actual voucher-bearing file before cutover.

## Storage and portal behavior

Migration `20261027031358_AddLegacyBillApprovalHistory` adds `AppLegacyBillApprovals`, with tenant/student/term uniqueness, tenant/term/status lookup index, original approval/login/acceptance values, resolved student/term identifiers, and source hash/line provenance. It has no portal-user or student-profile dependency, so students who have never signed in are included. Existing CampusFlow approvals, signed agreements, PDFs and payment data are untouched.

`/BillApprovalHistory` displays the current student's full native and legacy history. It uses the authenticated/current student-view profile and explicit tenant checks; there is no request parameter for arbitrary student IDs. Existing authorized student impersonation can view that student's history. Legacy records are read-only, separate from native PDF generation and the existing native approval reset operation.

For the active approval page and registration journey, an existing native record takes precedence; a valid dated approved legacy record is used only when no native record exists. An imported approval does not ask the student to sign/pay again through that approval workflow, and does not claim that a legacy PDF is being generated. Historical names/dates remain visible for closed terms through the history page.

BibliU eligibility preserves the current native rule (`AcceptedAt` plus `Status=Approved`) and adds dated, approved legacy records for the selected real term. Any native record for the same student/term prevents a legacy override, including native drafts or pending states. False legacy flags, missing acceptance dates and term 0 are excluded. Tenant isolation and the Elements source filters remain enforced. No export settings or production flow definitions change.

## Import utility and safeguards

`src/CampusFlow.LegacyBillApprovalImport/` is a standalone console project included in solution builds, not a web endpoint or startup data seed. It reads a consistent source-byte snapshot, validates the exact header and row values, rejects duplicate keys, resolves all mappings before writing, and previews by default. Credentials come from an explicitly supplied private configuration file or the explicitly named Azure web app's current configuration; credentials and source rows are never printed.

Example preview (substitute the private export path):

```sh
dotnet run --project src/CampusFlow.LegacyBillApprovalImport --configuration Release -- \
  --source '/private/path/approved-bills.tsv' \
  --tenant 3a236d25-0541-8fa3-25ec-fecc83432a1a \
  --azure-app app-campusflow-prod-001 --resource-group rg_campusflow
```

Applying also requires `--apply`, `--expected-sha256` with the reviewed hash, and `--expected-rows 83167`. The utility checks the target tenant and schema, serializes concurrent imports with a transaction-scoped advisory lock, refuses differing or deleted existing legacy records, and inserts only new records in one transaction using a temporary staging table. Matching existing records are left untouched, including their original import provenance. It never updates native approvals, creates accounts, posts payments, generates/uploads documents, or uploads bookstore files. Running the same source afterward in preview mode should report zero inserts, 83,167 unchanged, and zero conflicts.

The PostgreSQL write path has been built and reviewed but not executed against production. Docker was unavailable for a disposable local PostgreSQL run. Persistence, timestamp retention, tenant isolation, and duplicate-key enforcement were tested using the local SQLite integration fixture; the production preview exercised the complete source parser, live mappings, target tenant, native overlap and schema checks without writes.

## Validation and authorized rollout

142 tests passed: 61 domain, 5 persistence, 76 web. Coverage includes source parsing/nulls/precision, malformed and duplicate rows, eligibility exceptions, storage without accounts, tenant isolation, database uniqueness, native precedence in BibliU, and current-student history access. The importer builds in Release; the EF pending-model check reports no changes. A narrow migration SQL script was generated privately and contains only the new history table/indexes and its migration-history entry.

After approval:

1. Commit the reviewed changes and recheck the production migration baseline.
2. Apply only the prepared history migration in a transaction; do not run broad data seeds.
3. Run the approved source import with the exact hash/count and verify stored flags/timestamps/counts. Rerun the default preview to prove idempotency.
4. Deploy the reviewed web changes. Verify health, own-student/authorized impersonation history, existing-native precedence, and export authorization.
5. Run only manual test flow copies and verify the voucher-bearing user file. Keep the three production flows and BibliU delivery unchanged.

If deployment fails, the old application can continue with the added history table unused; the import does not alter native records. Do not remove imported production records or apply a down migration without a separate recovery decision.
