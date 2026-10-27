# BibliU Power Automate test setup

Production flow definitions and states were backed up and compared after creating the test copies. All three originals remained unchanged. Test copies were created in the existing Nelson Production Power Automate environment, with manual triggers and state Stopped. No delivery connections or scheduled triggers are present.

| Test copy | Link |
| --- | --- |
| TEST - CampusFlow - Bookstore User Import File POS Exports | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/cd804745-636f-4538-9f31-ff48884e9857/details) |
| TEST - CampusFlow - Bookstore Course Schedule and Enrollment | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/04278fea-12d4-4d78-9c7c-d2fa55ba9903/details) |
| TEST - CampusFlow - Bookstore Course Import File POS Exports | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/e51b19a1-608f-43e5-9d46-7e2bb7f1cbc3/details) |

## Test flow changes

* Replaced CAMS SQL reads with CampusFlow export API calls. The schedule/enrollment copy makes one `all` request for its paired outputs.
* Retained the existing field ordering, tab/CSV formatting and line endings. Fixed the test POS course mapping from SQL-encoded `Location_x0023_` to API `Location#`, and corrected `CourseTItle` to `CourseTitle`.
* Removed every SFTP action and connection reference. Final file composition remains in Power Automate; no uploads can occur from these definitions.
* Added secure inputs/outputs to HTTP token and export actions and supported data-action settings. Compose supports secure inputs only; its downstream sensitive inputs are protected as well. A summary exposes counts and configuration version, not file contents or credentials.
* Manual triggers accept no credential input. All three copies use the connected `BibliU test credential vault` connection to retrieve the secret at run time. Secret and token actions protect both inputs and outputs; no secret value is embedded in definitions.

## Authentication and deployment

The live CampusFlow server advertises the `client_credentials` grant and token endpoint `https://my.nelson.edu/connect/token`. With explicit approval, the dedicated confidential client `CampusFlow_Bibliu_PowerAutomate_Test` was created with only token endpoint, client-credentials grant and `CampusFlow.Bibliu` scope permissions (resource/audience `CampusFlow`). Only `CampusFlow.Admin.BibliuExport` was granted to provider `C` for this client within the nelson tenant. It has no settings-edit grant and no host grant.

With explicit approval, `kv-cf-bibliu-test-001` was created in `rg_campusflow`, Central US. Its only secret is `CampusFlow-Bibliu-PowerAutomate-Test`, generated in memory and stored directly in the vault, with a 90-day vault-secret expiry. The setup account has get/set/list secret permissions in this vault. Vault expiry is metadata; rotate or revoke the OAuth client credential before expiry rather than assuming vault expiry revokes the client. No secret is in tracked files or flow definitions. The Power Automate Key Vault connection is connected and wired only into the three test copies. The approved popup change applies to `make.powerautomate.com`.

With explicit approval, production BibliU configuration was enabled for billing Fall 2026 (268), BibliU Spring 2027 (269), store 123 and InSite store 241. Migration `20261027020336_AddBibliuConfiguration` and the original BibliU implementation were previously deployed. The existing admin role has settings permission only; export access was not added to that role.

ABP 10.5's built-in client provider checks host grants; its permission checker also determines tenant-only permission eligibility from the principal's tenant claim, which default client tokens lack. The approved correction adds a BibliU claims handler and tenant client permission provider. It requires the BibliU scope and a tenant export grant before binding a token, and requires matching token/resolved tenant plus the same grant to read exports. Permission definitions retain their tenant-only restriction.

Commit `e9b3779eacf1aa27b03630b1d3a6d16889b2d22d` was deployed successfully through [production run 40441261429](https://github.com/tjdavisbz/CampusFlow/actions/runs/40441261429). The workflow's build and tests passed; the local web suite passed 73 tests. Production health returned HTTP 200. No further database migration was required.

## Initial verification before legacy approval import

* Fresh client tokens contain the nelson tenant claim and the BibliU scope. The live export API returned HTTP 200 for all four feeds. The settings page returned HTTP 403 for this client.
* A token without the BibliU scope could not read exports (HTTP 401). A request for unapproved scope `CampusFlow` was rejected by the token endpoint (HTTP 400, `invalid_request`). Local tests cover cross-tenant denial and host denial through the real permission checker, bypassing the shared test fixture's always-allow stub.
* All three manual test copies completed successfully using the Key Vault connection and were turned off afterward. Final readback confirmed state `Stopped`, manual-only triggers, protected token/secret/export actions, and only the dedicated Key Vault connection reference.
* All three production definitions, connection references, display names and states were re-read after the tests and matched the preserved original backups. No BibliU upload occurred.

| Output | Data rows | Fields | Header | UTF-8 bytes |
| --- | ---: | ---: | --- | ---: |
| Bookstore_UserImportFile.txt | 6,838 | 9 | None | 649,610 |
| CourseImport_123.csv | 770 | 17 | None | 130,129 |
| CourseSchedule.csv | 1,720 | 16 | Yes | 278,419 |
| CourseEnrollment.csv | 10,729 | 12 | Yes | 1,804,917 |

All four files were reconstructed privately from the API response using the deployed test definitions' exact mappings and formatting. Their serialized Compose-output MD5 hashes and byte counts matched Power Automate's successful run metadata. This verified file content without disabling secure logging or retrieving secret/token action outputs. Power Automate's computed-output JSON transport includes a UTF-8 BOM; the file strings themselves do not. CSV/tab round trips matched every selected source value. Field order, headers, embedded-quote escaping, LF line endings, POS field widths and padded identifiers passed. The user file ends with LF; the other files retain the originals' lack of a final LF.

Enrollment output includes 8,115 Active and 2,614 Inactive rows. User output contains 6,838 AM identity records and **no AE voucher records**. A read-only aggregate query confirmed **zero CampusFlow bill records and zero eligible approved bills for nelson billing term Fall 2026 (268)**. Thus absence of vouchers follows the configured eligibility rule; live voucher amount/record verification remains outstanding. Existing local tests exercise the voucher rules and a previous synthetic reader preview exercised voucher generation without changing production data.

Successful test run IDs:

* User: `08584085367867851765339762677CU25`
* Schedule/enrollment: `08584085367619243521811482656CU12`
* Course: `08584085367293047514677891871CU27`

Private test files are under ignored `tools/local/bibliu-flow-test/files/` (directory mode 700, files mode 600). Source response, run metadata, checksums, and aggregate validation results are retained privately in the same ignored setup directory. Student data is not committed.

## Verification after legacy approval import

With explicit approval, commit `45fcb2726bfc04e714a4030fea03f938f32954d1` added the legacy approval archive and eligibility lookup. All 83,167 supplied records were imported and reconciled in nelson production, then deployed successfully through [production run 40444333879](https://github.com/tjdavisbz/CampusFlow/actions/runs/40444333879). See [the import record](legacy-bill-approval-import.md) for preserved exceptions and native-record precedence. The configured terms/stores and all test flow definitions remained unchanged.

The live API returned 10,574 user rows: 6,838 AM identities and 3,736 AE voucher rows for 1,868 eligible students. There are 1,869 dated approved legacy bills for Fall 2026; one student's missing row in the existing term-status source view excludes that student under the existing export filter. No eligibility or financial source data was altered to make the counts match.

All three manual test copies succeeded again and were stopped afterward:

* User: `08584085342712530969296554026CU24`
* Schedule/enrollment: `08584085342457388935396398548CU16`
* Course: `08584085342137702094591291996CU16`

| Output | Data rows | Fields | Header | UTF-8 bytes |
| --- | ---: | ---: | --- | ---: |
| Bookstore_UserImportFile.txt | 10,574 | 9 | None | 1,004,530 |
| CourseImport_123.csv | 770 | 17 | None | 130,129 |
| CourseSchedule.csv | 1,720 | 16 | Yes | 278,419 |
| CourseEnrollment.csv | 10,729 | 12 | Yes | 1,804,917 |

All four source-based private file reconstructions matched the new Power Automate Compose-output hashes and byte counts. Every selected field also passed the CSV/tab round trip, width, padding, header and line-ending checks described above. Secure action logging remained enabled. The dedicated client still received HTTP 403 for settings, a token without the BibliU scope received HTTP 401 for exports, and an unapproved scope was rejected with HTTP 400. Production health returned HTTP 200. The history page was visually checked through the existing authorized read-only student view, which was exited afterward.

Final management readback confirmed all three originals still matched their preserved definitions, connection references, names and states. All three test copies were `Stopped`, manual-only, protected and connected only to the dedicated Key Vault connection, with no delivery actions.

## Subsequent production cutover

After the voucher-bearing file was verified, the user accepted the format/rule checks and explicitly authorized production cutover. The three existing production flows now use the verified API steps and original delivery destinations/schedules. All three live runs and all four SFTP uploads succeeded; the test copies remain stopped. See the [production cutover record](bibliu-production-cutover.md) for current state, run IDs, file checks and rollback details.


Private original/test definitions and manifests are retained under ignored `tools/local/bibliu-flow-test/`; credentials and student rows are not included in this document.
