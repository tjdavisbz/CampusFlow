# BibliU Power Automate test setup

Production flow definitions and states were backed up and compared after creating the test copies. All three originals remained unchanged. Test copies were created in the existing Nelson Production Power Automate environment, with manual triggers and state Stopped. No delivery connections or scheduled triggers are present.

| Test copy | Link |
| --- | --- |
| TEST - CampusFlow - Bookstore User Import File POS Exports | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/cd804745-636f-4538-9f31-ff48884e9857/details) |
| TEST - CampusFlow - Bookstore Course Schedule and Enrollment | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/04278fea-12d4-4d78-9c7c-d2fa55ba9903/details) |
| TEST - CampusFlow - Bookstore Course Import File POS Exports | [Open flow](https://make.powerautomate.com/environments/573d9345-5c66-ed17-b685-7ab1054c1512/flows/e51b19a1-608f-43e5-9d46-7e2bb7f1cbc3/details) |

## Prepared changes

* Replaced CAMS SQL reads with CampusFlow export API calls. The schedule/enrollment copy makes one `all` request for its paired outputs.
* Retained the existing field ordering, tab/CSV formatting and line endings. Fixed the test POS course mapping from SQL-encoded `Location_x0023_` to API `Location#`, and corrected `CourseTItle` to `CourseTitle`.
* Removed every SFTP action and connection reference. Final file composition remains in Power Automate; no uploads can occur from these definitions.
* Added secure inputs/outputs to HTTP token and export actions and supported data-action settings. Compose supports secure inputs only; its downstream sensitive inputs are protected as well. A summary exposes counts and configuration version, not file contents or credentials.
* Current test trigger accepts a masked secret at run time. No secret value is embedded in the definition. Replace this temporary mechanism with the approved Key Vault connection before running the copies.

## Authentication preparation

The live CampusFlow server advertises the `client_credentials` grant and token endpoint `https://my.nelson.edu/connect/token`. With explicit approval, the dedicated confidential client `CampusFlow_Bibliu_PowerAutomate_Test` was created with only token endpoint, client-credentials grant and `CampusFlow.Bibliu` scope permissions (resource/audience `CampusFlow`). Only `CampusFlow.Admin.BibliuExport` was granted to provider `C` for this client within the nelson tenant. It has no settings-edit grant and no host grant.

With explicit approval, `kv-cf-bibliu-test-001` was created in `rg_campusflow`, Central US. Its only secret is `CampusFlow-Bibliu-PowerAutomate-Test`, generated in memory and stored directly in the vault, with a 90-day vault-secret expiry. The setup account has get/set/list secret permissions in this vault. Vault expiry is metadata; rotate or revoke the OAuth client credential before expiry rather than assuming vault expiry revokes the client. No secret is in tracked files or flow definitions. The Power Automate Key Vault connection is not yet complete: Safari blocked its sign-in popup. Site popup approval has been received, but the browser is switching to an unrelated locked private window; the user has been asked to restore the normal Power Automate window.

With explicit approval, production BibliU configuration was enabled for billing Fall 2026 (268), BibliU Spring 2027 (269), store 123 and InSite store 241. Migration `20261027020336_AddBibliuConfiguration` and the original BibliU implementation were previously deployed. The existing admin role has settings permission only; export access was not added to that role.

Live token issuance succeeds for the new client, but the deployed export endpoint returns HTTP 403. Investigation confirmed ABP 10.5's built-in client provider explicitly switches to host context to check grants; the approved grant is tenant-scoped. Its permission checker also determines tenant-only permission eligibility from the principal's tenant claim, which default client tokens lack. A local correction adds a narrowly scoped BibliU claims handler and tenant client permission provider. It requires the BibliU scope and a tenant export grant before binding a token, and requires matching token/resolved tenant plus the same grant to read exports. Permission definitions retain their tenant-only restriction. This correction needs deployment approval; no additional deployment has occurred.

## Validation still required

The local web suite passed 73 tests, including new scope, tenant-binding, permission and cross-tenant denial checks using the real application permission checker (the shared test fixture's always-allow checker is deliberately bypassed). All three production definitions, connection references, display names and states were re-read and matched the original backups after client provisioning.

Remaining: deploy the tested authorization correction once approved, complete and wire the Key Vault connection, obtain fresh tokens and verify allowed/denied live API operations, run only test copies manually, securely retrieve or capture generated test files for comparison, and verify headers, field counts, quotes, line endings, fixed-width values, row counts, IDs, inactive enrollment and voucher amounts. Do not connect production delivery or edit original flows until file verification is approved.

Private original/test definitions and manifests are retained under ignored `tools/local/bibliu-flow-test/`; credentials and student rows are not included in this document.
