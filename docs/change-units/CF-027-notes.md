# CF-027 — separate meal-plan sales tax

Deployed November 9, 2026 (America/Chicago) in nonprod and production. Source is committed and pushed. Current release verification is recorded in the environment register.

The requested rate is 8.25% of the assigned plan’s Elements base price, rounded to two decimals away from zero at a midpoint. All seven documented plan amounts are tested. The tax is a separate debit during writable, unapproved Spring 2027-and-later bill review, after recalculation and before the bill is read. The cutoff uses `TermCalendar.Term`; it does not sort display names or calendar IDs.

Each Elements assignment has one durable tenant-scoped attempt and a deterministic `CFMT` reference. A repeated review may confirm an existing matching charge, including a posted ledger charge. An uncertain create is not automatically resent. Live API and replica visibility are both checked before approval can continue. Existing/manual conflicting tax requires Billing review; existing entries are never updated or deleted by this gateway. Removing a plan does not automatically refund its base or tax charge, and the staff notice says so. Approved/legacy-approved and read-only impersonated reviews do not post tax.

## Deployment requirements

1. Apply only the reviewed `CF-027-migration.sql` (hash in status). It creates the tax-attempt audit table and unique index; do not run broad production seeding. The exact migration is now applied in both environments; unrelated schema/history were preserved.
2. For each environment separately, verify its active `TAX — Texas State Sales Tax` transaction document. Read-only discovery found ID 261 in both; verify again rather than copying an ID blindly.
3. Create or verify one dedicated **open Billing** batch with comment `CampusFlow meal-plan sales tax`, `SourceModuleID=2`, and `TermBased=1`. The API generates the batch name; no fixed name is assumed. Existing StudentLife meal-plan batches use module 15 and are not reused. No matching production tax batch existed during discovery.
4. The inspected Billing API exposes `POST /api/billing/cashier-entry/batches/batch-master` with `BatchMasterRequest` (`campus`, `comment`, `mod`, `termBased`). Prepare a scoped request using the verified environment’s gateway and Billing headers, then confirm the returned batch in that same replica before configuring its ID. Do not retry an unknown setup result blindly; inspect existing batches by the exact comment first.
5. Set `MealPlanTax:Enabled=true`, `MealPlanTax:BatchMasterId`, `MealPlanTax:TransactionDocumentId`, and only the environment’s own `EnableNonProductionWrites` or `EnableProductionWrites` flag. The opposite flag must be false. The feature defaults off; deployment is not complete until configuration and acceptance are verified. If it is disabled after rollout, restrict bill approval or verify tax manually rather than assuming new tax is being posted.
6. Populate/verify the missing nonprod Spring meal catalog as needed, then use controlled nonprod test data and a writable student flow. Read-only impersonation intentionally does not create charges. Verify exact tax amount/document/reference, inclusion in bill totals, repeat-review idempotency, and recovery/readback. Do not fabricate production bill approvals for testing.
7. Activate production only after controlled nonprod write acceptance. Nonprod controlled posting is verified and cleaned; its dedicated batch remains for deployment. No production student data, portal setting, migration or message changed during preparation.

Rolling application code back must preserve audit records and posted tax history; disabling/removing the code does not refund charges. The generated Down migration is not part of a normal application rollback.

## Verification

47 new tax checks are included in the combined release: calculation, immutable basis, durable uniqueness, concurrent reviews, lost responses, safe preflight retry, exact gateway/tenant/student/term/document/amount, posted-charge preservation, DI resolution and direct approval/payment guards. Full combined verification: 1,017 production-base / 1,011 nonprod-base tests and 19 browser scenarios in each. Actual nonprod tax posting passed with $2,475 base/$204.19 tax. A successful response was deliberately lost; new coordinator instances recovered the same reference and repeated reviews produced no duplicate POST. All temporary test assignment/base/tax records were removed. Evidence: `CF-027-nonprod-tax-canary.json`. Signed-in deployed-page acceptance remains.

Nonprod setup prepared: eight Spring 2027 test catalog entries (IDs 9–16) from nonprod-only templates, dedicated Billing batch `2147370696` at main campus `0`, TAX document `261`. Production must obtain and verify its own batch ID; never copy the nonprod destination.

Completed rollout: production batch `2147370553`, independently created/verified; nonprod batch `2147370696`. Both have matching own-environment tax flags, TAX 261, and the audit table/index. The real nonprod PostgreSQL canary passed. Production audit rows were zero at final verification; the first legitimate production student review remains staff acceptance.
