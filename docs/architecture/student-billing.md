# Student billing

CampusFlow presents student billing through the provider-neutral
`IStudentInformationSystemBillingLookup` boundary. Nelson's initial implementation reads
Thesis Elements using the student's private external identifier (`StudentUID`). Public UI
must continue to display the student-facing `StudentID`, never `StudentUID`.

## Thesis Elements transaction source

Thesis Elements stores student account activity in two locations:

- `Billing` contains transactions posted to the ledger.
- `BillingBatch` contains transactions entered in a batch but not yet distributed to the
  ledger. CampusFlow labels these transactions **Pending**.

The CampusFlow query reproduces the semantics of
`3DTech_Billing_vTransactionsLedgerAndBatch` as a parameterized `UNION ALL`; the database
view is not expected to exist. Both branches join `Student`, `TermCalendar`, and `Transdoc`.
The full reference view also joins batch history, glossary, accounts-receivable type, and
payment-plan metadata. Those joins can be added to the provider when a feature needs the
additional fields.

The common identity and display fields are:

- Source location (`Ledger` or `Batch`)
- Source transaction ID
- `StudentUID`
- `TermCalendarID`, `Term`, and `TextTerm`
- Transaction date and description
- Debit, credit, and `ShowAmount`
- `Transdoc.ReportFlag`
- Ledger void/reversal status

All queries must filter by the authenticated student's linked `StudentUID` using a SQL
parameter. Billing data is read-only.

## Student-facing rules

- Include only transactions whose `Transdoc.ReportFlag` is `Yes`. Other rows are internal
  accounting activity and can produce a balance different from the established student
  ledger. For StudentUID `13465`, this rule produces the verified Fall 2024 balance of
  `($223.96)`; including non-reportable rows produces `($230.49)`.
- Use `ShowAmount` as the signed balance movement. Debits are positive movements and credits
  are negative movements; the `Debits` and `Credits` columns are presentation values.
- `BillingBatch` rows contribute to the pending balance and overall balance and are visibly
  labeled **Pending**.
- `Billing` rows contribute to the posted balance and are labeled **Posted**.
- Ledger rows for which `Voided = 'Yes'` or `Reversing = 'Yes'` remain available for audit
  display but do not contribute to posted, term, running, or overall balances.
- Overall balance equals posted balance plus pending balance across all terms.
- A term balance and its running balance are calculated from that term's reportable,
  non-voided transactions in transaction-date order.
- Negative balances are credit balances and use accounting notation, for example
  `($223.96)`.

## Term experience

Billing does not use a global user-selected term. Transactions are grouped by term and shown
in descending term-code order. The current term and later terms appear in a dedicated
current/upcoming panel; earlier terms appear separately as account history. A term's net
balance appears in current/upcoming headers and in the expanded detail footer, while
historical headers omit it because CAMS does not maintain independent payable balances by
term. Only the current term is expanded initially; future and historical terms are collapsed.
The overall summary remains the authoritative account balance across all reportable activity.
This lets students review current, upcoming, and historical
activity together without changing hidden portal state.

The dashboard's configured/current term is separate display context and must not filter the
billing account automatically.

During development against the stale Thesis Elements copy, Nelson sets
`StudentInformationSystems:Providers:ThesisElements:CurrentTermOverride` to `B25L` (Summer
2025). Removing that setting restores the normal date-based current-term rule. This is a
temporary data-fixture override and is not the future tenant-admin term configuration.

## Bill approval

Bill approval will build on the same normalized transaction and balance rules. Before an
approval is accepted, CampusFlow must make clear:

- which term or terms are being approved;
- the posted amount, pending amount, and overall amount included;
- that pending batch entries can change before ledger posting; and
- the exact transaction/balance snapshot and approval timestamp.

Approval records belong in the CampusFlow database, keyed to the tenant, ABP user, student
profile, and applicable SIS term identifiers. Approval must not be inferred merely from a
page visit, and a later balance change must not silently rewrite the historical snapshot.

## Payments

Payment processing is a separate integration from billing display and bill approval. Nelson
uses PayPal Payflow Pro. Prefer secure-token hosted checkout or Transparent Redirect if the
merchant account supports it; otherwise the direct Payflow API can reproduce the existing
server-hosted flow with the corresponding PCI obligations.

CampusFlow must never persist or log card numbers or CVV values. It should retain only safe
payment-attempt metadata such as amount, status, CampusFlow idempotency key, Payflow
transaction identifiers, and timestamps. Convenience-fee policy (currently 2.85% in the
legacy portal) must be tenant configuration rather than hard-coded billing logic.

## Development data caveat

The configured Thesis Elements read-only database is a stale pre-production copy ahead of
the October 5, 2026 go-live. Missing future batch activity is not evidence that the union
query is incorrect. Fall 2024 data for the verified student is the current reference set for
checking transaction visibility and balance math.
