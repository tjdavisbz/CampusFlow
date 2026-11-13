# Shared housing and meal-plan batches (CF-043)

Housing already writes to the exact `Housing:ElementsBatchMasterId`, validated as
`HousingCharges` / `Housing Charges`, cashier module 2, open and term-based.
That ID is shared across students and terms. No new housing batch is created.
Zero-dollar Elements commuter rent rows are not CampusFlow housing fees; the
billing-preserving commuter recovery path remains unchanged.

Meal-plan assignment creates a vendor-selected Student Life billing entry.
CF-043 moves that existing entry to `MealPlans:SharedBatchMasterId` after a
successful assignment and reconciles existing matching base/tax entries during
bill-review preflight. The destination is one exact, environment-specific ID,
shared across students and terms, with comment `Meal Plan Charges`, cashier
module 2, open and term-based. The application never creates batch headers.

Tax remains a separate 8.25% line and is created in that same destination. The
existing Spring 2027 tax cutoff remains unchanged. Retain
`MealPlanTax:BatchMasterId` as the legacy tax destination while unposted taxes
remain there. Reviewed removal recognizes both the original vendor/legacy
formats and the configured shared batch. Posted ledger transactions are not
moved or deleted. Amounts, entry IDs, documents, references, dates and payment
plan fields are preserved by the edit request. Only matching unposted charges
with live/reporting agreement are eligible; manual, paid, distributed, foreign
currency and ambiguous entries require billing review.

If `MealPlans:SharedBatchMasterId` is absent or zero, existing behavior remains.
Enabling requires the existing matching environment's tax write gates and a
verified shared batch; never copy a batch ID between production and nonprod.
The API lacks a conditional update token, so staff must avoid concurrently
editing/posting the same reviewed charges. A lost edit response is recovered by
rereading the same charge ID; no replacement charge is created.

Deployment preparation must verify/select one shared meal-plan batch in each
environment and set its exact ID. Reuse the existing housing destination.
Historical charges are not bulk migrated by deployment: reconciliation is scoped
to the student/term being processed. Elements may leave empty generated batch
headers after its entries move; automatic header deletion is deliberately omitted
because it could remove another user's newly added entries.
