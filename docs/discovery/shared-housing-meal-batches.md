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

## CF-046 / CF-047 follow-up

Elements can commit a billing edit or tax insert and then return an error while
building the response. CampusFlow now reads back the same entry ID and reviewed
fields after an edit response failure, or the exact durable tax reference after
an unconfirmed tax send. It does not resend the mutation. An unmatched amount,
identity, destination or response still stops the operation for review. Paid
selection confirms tax immediately, using the same coordinator as bill review.
The dedicated destination is checked before saving a new local choice or sending
an assignment. Explicitly unassigned payment-plan metadata is represented as
zero in the legacy billing edit request; positive plan IDs are preserved.

The deployed Elements distribution procedure deletes an empty batch header after
posting its entries. A configured ID is therefore a preferred starting point,
not an everlasting header. If it disappears or closes, CampusFlow may use one
unique open, term-based cashier batch with the exact approved comment: `Housing
Charges` or `Meal Plan Charges`. It never substitutes a Student Life source-15
batch, chooses between ambiguous candidates, or accepts a repurposed configured
ID. Staff prepare the replacement batch through normal Billing operations;
CampusFlow does not create or delete headers. A missing write destination stops
before a new assignment is sent. Meal-plan removal review validates the tax
document and actual existing charges without requiring an open destination for
future charges.

`Housing_Fees` also contains meal-plan, meal-tax and tracking entries. Housing
reconciliation now uses only transaction documents belonging to the reviewed
housing rate mappings and the intentional-single document. Meals and their tax
are not housing conflicts and are never housing replacement/deletion targets.
Exact generated-rent cleanup also discovers Student Life source-15 batches.

The Elements distribution procedure has no zero-amount exclusion. Zero-dollar
commuter rent rows can be posted and, when reportable, displayed on the bill;
they do not change the balance. Removing verified zero-only vendor batches does
not remove the room assignments. It does not trigger a complete housing-batch
rebuild either. Do not delete nonzero/manual/posted charges or the active shared
destinations as a shortcut; review their contents and prepare any replacement
shared destination before new charges are needed. Historical commuter recovery
continues to preserve billing and does not bulk clean up those old zero rows.
