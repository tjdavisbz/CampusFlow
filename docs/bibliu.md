# BibliU bookstore integration

## Admin configuration

Open **Admin → BibliU Bookstore** (`/Admin/Bibliu`) in a tenant context.
`CampusFlow.Admin.BibliuConfiguration` is required. Host context cannot configure
or read tenant exports. Existing admin role permission behavior applies; no new
users, credentials or access grants are created by this feature.

Select the billing/voucher term and the BibliU term independently from Elements.
The screen previews related schedule/enrollment terms. Store numbers are
three-digit strings (leading zeros are retained). InSite store number is retained
but unused by the four current file contracts. Configuration starts disabled;
saving does not run a flow, upload a file, or change Power Automate.

Settings are fully audited entities, with one live configuration per tenant and
optimistic concurrency. A stale form must be reloaded before saving. There are
no seeded production terms. Apply migration `AddBibliuConfiguration` to the
intended CampusFlow database before using the screen. Production migration and
test-configuration status are recorded in the [test setup record](discovery/bibliu-power-automate-test-setup.md).

## Export API

`GET /api/integrations/bibliu/exports/{kind}`

Kinds: `users`, `courses`, `schedule`, `enrollments`, or `all`.
Requires authenticated access with **CampusFlow.Admin.BibliuExport**, independently
of permission to edit settings, plus a resolved tenant. Use the application's
existing authentication and tenant-resolution mechanisms. A tenant header alone
does not authenticate or grant access. Configure a dedicated approved integration
identity before connecting Power Automate; never put tokens or passwords in URLs.

For client credentials, use a confidential client allowed the token endpoint,
`client_credentials`, and scope `CampusFlow.Bibliu` (resource/audience `CampusFlow`).
Grant `CampusFlow.Admin.BibliuExport` with provider `C` and the client ID **within
the intended tenant**, not at the host. ABP's built-in client permission provider
checks host grants and does not honor this tenant grant. The BibliU claims handler
binds a scoped client token to the issuing tenant only after checking that tenant's
explicit export grant; the BibliU permission provider checks the same grant and
requires its tenant claim to match the resolved tenant. Neither component grants
settings access. Client grants and secrets must be provisioned separately with
approval. Keep the secret in the approved vault, protect token/secret action
inputs and outputs, and obtain new tokens after deploying the claims handler.

Responses contain `configurationId`, `configurationVersion`, `generatedAtUtc`,
`billingTerm`, `bibliuTerm`, `expandedTerms`, and `exports`. The latter maps each
requested kind to an array of rows. Row keys retain the legacy column names,
including `RecordCode`, `NumericDataField`, `CRN`, and `EnrollmentStatus`. Values
are strings or null. Empty valid results are empty arrays. Example shape only:

```json
{
  "configurationId": "00000000-0000-0000-0000-000000000000",
  "configurationVersion": "version",
  "generatedAtUtc": "2026-10-27T00:00:00Z",
  "billingTerm": "Fall 2026",
  "bibliuTerm": "Spring 2027",
  "expandedTerms": ["Fall 2026", "Spring 2027", "Summer 2027"],
  "exports": {"users": []}
}
```

One request uses one configuration and approval selection. Prefer `all` when
retrieving the paired schedule/enrollment feeds together. Source queries are
sequential reads, not an atomic SQL snapshot; replica refreshes or changes can
occur between reads. `generatedAtUtc` is generation time, not replica freshness.
There is no saved run/snapshot API in this initial implementation.

Rows preserve fixed-width POS fields before the existing flows' formatting steps.
The API does not generate final files or perform SFTP delivery. The existing flows
must eventually consume `exports.<kind>` instead of SQL `body/value`; do not wire
this response directly into SFTP without retaining and testing the formatting.
Do not trim spaces from POS fields. Dates use the legacy MM/dd/yyyy text format.

Missing/disabled configuration, missing terms, source failure, invalid POS
identifiers, or more than 100,000 output rows per feed fail the request. No partial
response is returned. The API intentionally does not truncate or paginate live
results; revise the contract deliberately if volumes grow beyond the limit.
Responses disable caching and ABP action-payload auditing. When connecting flows,
secure their inputs/outputs so exported student and financial information is not
retained in ordinary run logs.

## Preserved rules

* Identity names and POS courses use the selected BibliU term. Schedule/enrollment
  expands Spring Y to Fall Y-1/Spring Y/Summer Y, Summer Y to Spring/Summer/Fall Y,
  and Fall Y to Summer Y/Fall Y/Spring Y+1; only existing terms are included.
* Vouchers require an accepted CampusFlow bill with `Status=Approved` for the
  same tenant and billing term. Drafts and AwaitingPayment do not qualify. Legacy
  approval-table existence is deliberately replaced with this agreed approval rule.
* Amount is `max(0, max(500, -balanceIncludingPendingAid) - max(0, BOOK usage))`.
  Balance includes the billing term plus previous terms, with legacy report,
  void/reversal and pending-aid eligibility filters. Current balance and usage
  come from Elements, not the frozen bill-acceptance financial snapshot.
* Voc Rehab hold selects event suffix 200; otherwise credit above $500 selects
  100, and other balances select 500. Hold does not separately override amount.
* Legacy positive BOOK usage aggregation includes ledger and batch without new
  void/reversal exclusions. Home address existence gates user rows; active Local
  address rows supply enrollment email. Multiple distinct email/faculty rows retain
  legacy DISTINCT behavior. W grades and dropped courses produce inactive rows.
* The historical 12/09/2025 → 12/12/2025 date exception remains, as requested.
* Numeric student IDs are padded to 13 digits after the store number. IDs that
  pass legacy SQL ISNUMERIC but cannot safely fit the digits-only POS contract
  fail the export. Amounts are rounded to cents with midpoint away from zero.

## Validation and rollout

Focused tests cover voucher thresholds/usage, hold priority, term expansion,
identifier widths/cents, approved-vs-draft rules, configuration validation,
host/disabled guards, tenant/term filtering, permission attributes and payload
logging controls. All four query shapes were validated against the reporting
replica; readers were exercised without delivery, including a synthetic voucher
eligibility preview whose identities and amounts were suppressed.

Production flows remain unchanged. Before cutover, verify identifier continuity
and final formatted files with BibliU and test through an approved staging
destination. See the test setup record for completed provisioning, validation,
and remaining rollout work. Test copies contain no SFTP delivery actions.

See [discovery findings](discovery/bibliu-elements-migration.md) for source mapping.
