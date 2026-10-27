# BibliU production flow cutover

## Completed state

On October 26, 2026 (Central time), the user explicitly authorized cutover after
reviewing the successful format and business-rule checks. All three existing
production flows in the Nelson Production environment
`573d9345-5c66-ed17-b685-7ab1054c1512` now read the CampusFlow export API and deliver
through their existing BibliU SFTP connection. All three are `Started`.

The three original flow IDs, names, recurrence definitions, Central time zone,
SFTP connection reference, folders, filenames and upload content references were
preserved. Legacy SQL actions and their connection references were removed from
these flows. The tested API/authentication and file-formatting actions were
copied exactly from the verified test copies, excluding the test summary.
Upload actions retain their existing transfer settings and now also protect
inputs and outputs. No application deployment, schema migration, new permission
grant, or term/store change was needed for this cutover.

| Production flow | Schedule retained | Files delivered |
| --- | --- | --- |
| [Bookstore User Import File POS Exports](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/3faf59fd-01fb-4b6f-833e-8e1381754306/details) | Hourly | `/123/inbox/Bookstore_UserImportFile.txt` |
| [Bookstore Course Schedule and Enrollment](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/3fe46c03-decb-49fe-b9c8-3c54a0da7304/details) | Daily at midnight, Central | `/CourseSchedule.csv`, `/CourseEnrollment.csv` |
| [Bookstore Course Import File POS Exports](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/7f5460a3-6ce5-44c3-9b5d-80a0b145c138/details) | Daily at midnight, Central | `/campus/CourseImport_123.csv` |

The flow engine accepted the unchanged existing SFTP reference. Connection
metadata itself is restricted to its owner; no ownership, credentials, or sharing
permissions were changed. Successful production upload actions verified that
the existing flows could still use the connection.

## API and credential dependencies

The flows use `https://my.nelson.edu/connect/token`, scope `CampusFlow.Bibliu`,
tenant `nelson`, and `/api/integrations/bibliu/exports/{users|all|courses}`.
The paired schedule/enrollment flow retrieves both feeds in one `all` request.
CampusFlow reads the Elements **reporting replica**, not the Elements direct API,
and combines that data with the CampusFlow term configuration and approval
history. The Power Automate flows do not connect directly to PostgreSQL or use
the legacy SQL gateway for these reads.

The cutover reuses the existing restricted integration identity
`CampusFlow_Bibliu_PowerAutomate_Test`, its secret
`CampusFlow-Bibliu-PowerAutomate-Test` in `kv-cf-bibliu-test-001`, and Key Vault
connection `7ea2d1c1860943fcaa726d1ccdbeaa7b`. Those names retain their test suffix,
but these resources now support production as well as the stopped test copies.
There is no embedded secret and no new access grant. The client retains only its
nelson export grant, with no settings-edit or host grant. Secret, token, export,
formatting and upload actions protect sensitive run data.

The vault secret's recorded expiry is `2027-01-25T02:42:40Z` (January 24, 2027,
8:42 PM Central). Rotate the existing OAuth credential and vault value before
that date; vault expiry metadata does not revoke the OAuth client credential.
The existing delegated Key Vault connection and SFTP connection remain operational
dependencies of Power Automate.

Configuration remains billing Fall 2026 (268), BibliU Spring 2027 (269), store
123, InSite store 241, version `83a05390b49947de8e9992fdea08de38`.

## Live verification

Fresh API access returned the expected four feeds and settings access remained
denied (HTTP 403). Each flow was paused while its definition was updated and read
back, then enabled with its original schedule. One verification run was requested
for each production flow. All three succeeded, including Key Vault retrieval,
token issuance, export reads, and every SFTP upload action.

| Flow | Successful production run |
| --- | --- |
| User | `08584085336859094324558643963CU09` |
| Schedule/enrollment | `08584085336540518321015323843CU01` |
| Course | `08584085336257516831876857775CU26` |

| File | Data rows | Fields | Header | UTF-8 bytes |
| --- | ---: | ---: | --- | ---: |
| Bookstore_UserImportFile.txt | 10,574 | 9 | None | 1,004,530 |
| CourseImport_123.csv | 770 | 17 | None | 130,129 |
| CourseSchedule.csv | 1,720 | 16 | Yes | 278,419 |
| CourseEnrollment.csv | 10,729 | 12 | Yes | 1,804,917 |

The user file includes 6,838 AM identity rows and 3,736 AE voucher rows for
1,868 students. Enrollment includes 8,115 Active and 2,614 Inactive rows.
All four production Compose-output MD5 hashes and byte counts matched private
reconstructions from the captured API response and the live flow mappings.
Selected source values, field counts, headers, user field widths, LF line endings,
and trailing-newline behavior also passed. No secure logging was disabled and no
secret/token action output was retrieved. The SFTP actions' success confirms file
transfer; BibliU's downstream import/processing status was not independently read.

Final management readback confirmed all three production flows `Started` with
the expected API definitions, schedules and delivery references. All three test
copies remain `Stopped`, manual-only and without delivery actions.

## Evidence and rollback

Private pre-cutover definitions, guarded payloads, post-update/final readbacks,
run metadata and file-verification results are under ignored
`tools/local/bibliu-production-cutover/20261027T035143Z/`. Directory mode is 700;
files are 600. The earlier source backups under
`tools/local/bibliu-flow-test/` were not overwritten. Student data and credentials
are not committed.

To stop delivery, turn off the affected production flow and inspect any active
run before retrying. A rollback of definitions must use the matching `.before.json`
file, preserving the original ID and connection references. The old CAMS database
is offline, so restoring the former SQL definition would not restore working
exports. Do not automatically restore it or rerun a partially delivered batch.
Restoring a flow definition does not undo an SFTP upload or BibliU processing.

See the [test setup record](bibliu-power-automate-test-setup.md) and
[legacy approval import record](legacy-bill-approval-import.md) for prior
implementation, authorization and validation evidence.
