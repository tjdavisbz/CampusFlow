# Azure non-production operations

Updated October 27, 2026 (America/Chicago). Non-production is deployed and open at
`https://my-np.nelson.edu`. Final staging run `40539547963` succeeded.
The owner authorized completing the non-production site at `https://my-np.nelson.edu`
and copying the production PostgreSQL application database **into non-production only**.

## Environment and promotion path

| Stage | Deployment | Application database / Elements |
| --- | --- | --- |
| Local | Local app | Separate local PostgreSQL; non-production Elements replica is the proposed read source |
| `develop` | No Azure deployment | Integration branch |
| `staging` | `app-campusflow-nonprod-001`, `https://my-np.nelson.edu` | `campusflow_nonprod`; non-production Elements |
| `main` | Existing production app, `https://my.nelson.edu` | Production PostgreSQL `postgres`; production Elements |

Promote reviewed changes `develop` → `staging` → `main`. A merge/push to `staging`
deploys non-production; a merge/push to `main` deploys production. Fixes made first on
production must be brought back into development/staging. The launch branch reconciles
production changes with the earlier development branch, preserving the Texas country
reporting migration. Unrelated work in the primary checkout is left untouched.

`.github/workflows/deploy-nonproduction.yml` is restricted to `staging`, including its
manual-dispatch job condition. GitHub environment is `nonproduction`. Its default
`slot-name: production` denotes the default slot of the **non-production app**.
A readiness setting blocks deployment until environment setup is verified. The
nonproduction GitHub environment also restricts deployment branches to `staging`.
The deployment identity trust matches GitHub's current subject, including immutable
owner/repository IDs; the older name-only subject caused the first launch attempt to
fail at sign-in and was corrected before retry.

## Azure resources

Subscription `3cc0ce6b-9bac-4bc8-81c2-8b22bd8d5059`, resource group `rg_campusflow`.

| Resource | Configuration |
| --- | --- |
| `asp-campusflow-nonprod-001` | Dedicated Central US Linux B1 plan; additional Azure hosting charges |
| `app-campusflow-nonprod-001` | .NET 10, Staging environment, HTTPS only, TLS 1.2 minimum, FTP disabled, Always On; `/health-status`; startup `dotnet CampusFlow.Web.dll` |
| `campusflow_nonprod` | Separate database on shared `psql-campusflow-prod-001`; dedicated login `campusflow_nonprod_app` |
| `id-github-campusflow-nonprod-001` | GitHub environment OIDC identity; Website Contributor on non-production app only |
| Entra `CampusFlow Non-Production` | Separate app registration `b61e876d-9316-4dfa-bed8-034528234d73`; separate secret |

The non-production PostgreSQL role owns its database/schema, has no superuser,
create-database, or create-role privileges, and must pass a check for zero production
table write/ownership privileges before any refresh. The owner explicitly approved
adding the non-production app's 12 previously unlisted exact outbound IPs to the shared
PostgreSQL server firewall on October 27. Existing rules are preserved; no all-internet
or all-Azure rule is used. This changes the shared server's network boundary, not its
production database data or production application settings.

## DNS and TLS

DNS provider: **DNS Made Easy / DigiCert DNS**. The owner added records on October 27
in the public `nelson.edu` zone (not the separately delegated `my.nelson.edu` zone):

- `my-np` CNAME → `app-campusflow-nonprod-001.azurewebsites.net.`
- `asuid.my-np` TXT → the new app's Azure custom-domain verification identifier.

Both records resolved and Azure accepted the hostname. A separate Azure managed
certificate was issued and bound through SNI, thumbprint
`D6948E318BE0F3CF7E7A60086B9C481D356EB824`, expiration April 27, 2027.
Preserve the direct CNAME for managed renewal. No existing production or email records
were changed. Verify Nelson split DNS if internal resolution differs.

`App__SelfUrl` and `AuthServer__Authority` use `https://my-np.nelson.edu`.
Entra callbacks include `/signin-oidc` and `/signout-callback-oidc` on that hostname,
plus the Azure hostname for diagnostics. OpenIddict uses an independently generated
signing/encryption certificate stored only in the GitHub nonproduction secret and its
Azure passphrase setting. The Entra secret and OpenIddict certificate expire in roughly
one year; plan their renewal. Signing credentials are separate from public TLS.

## Elements and safety settings

- SQL server: `sql-elm-nonprod-usc-replica.database.windows.net`; database `EL-NP-NEL-DB`.
  Existing replica credentials reused privately as authorized. Connection and SELECT
  access verified both locally and from the non-production Azure app console;
  1,512 tables visible. No SQL write was invoked.
- Tenant: `el-np-nel.thesiscloud.com`.
- Integration API: `https://app-elm-nonprod-usc-integrationapi.azurewebsites.net`.
- Gateway: `https://np-usc-gateway.thesiscloud.com`.

Both API hostnames were found in the confirmed non-production Elements web app's public
bundle. Integration API authentication returned HTTP 200 and a token using the existing
account with the non-production tenant. Gateway API metadata is accessible. No Elements
write operations were invoked during setup. Credentials are only in Azure settings.

The stale `B25L` term override is explicitly cleared. Payments remain disabled and
Payflow TestMode remains true; document-tracking writes remain disabled. PII logging is
disabled. Keep these non-production overrides after every database refresh. Local work
must use a separate writable CampusFlow database; shared non-production Elements writes
can affect staging tests, so keep local writes disabled until testing is planned.

## One-way PostgreSQL refresh

Use `tools/database/refresh_nonproduction.py` with authenticated Azure CLI and PostgreSQL
18+ client tools. Default is a read-only preview; `--apply` replaces non-production data.
No source/target/direction arguments are accepted.

**Allowed direction only:** production app `Default` connection, database `postgres`
→ non-production app `ConnectionStrings__Default`, database `campusflow_nonprod`.

Safeguards:

- Subscription, app names, database names, host, and destination role are fixed and
  checked before operations. Source sessions enforce transaction read-only.
- `pg_restore` receives only the non-production role, whose production table write and
  ownership privileges are checked to be zero; production schema/database CREATE
  privileges must also be absent. No production credentials are supplied
  to the restore command. No reverse-refresh operation is implemented.
- Non-production maintenance must be enabled. The tool stops only the non-production
  app during restore/validation and restarts only that app on success, still in
  maintenance. On failure it stays stopped for investigation.
- A private rollback dump of the previous non-production database is retained before
  a consistent production snapshot is taken. Dumps and diagnostics stay outside Git
  in a private temporary folder and contain sensitive production data.
- Restore uses full TLS validation, no source ownership/ACLs, exit-on-error and a single
  transaction. Azure-managed `azure` and `pgaadauth` extensions are excluded so platform
  components are never dropped/recreated. All source-dump table row counts are compared
  with the restored target.
- Non-production tenant connection-string overrides are removed after restoration so
  a copied tenant cannot redirect the site to a production database.

A refresh copies a point-in-time application database, not the SQL Server Elements
replica or uploaded App Service files. Independent Entra, signing credentials, API
endpoints, payment settings, and environment settings remain configured in Azure.
Apply any additional staging migrations only to non-production after the copy, then
verify health and tenant access before reopening. Never restore non-production to production.

## Verification and completion record

- Initial non-production schema and host/Nelson tenant seed completed October 27.
- Final launch build tests: 61 domain, 5 EF Core, 88 web tests passed (154 total).
- DNS, hostname binding, managed-certificate issuance, local replica SELECT and
  non-production Elements authentication verified.
- Deployment, the requested production-to-nonproduction refresh, Azure-side replica
  access, interactive sign-in/out and final reopening results will be recorded below.

Startup note: the first deployment retained the Azure placeholder until the explicit
CampusFlow startup command was configured. Health-check retries allow five minutes
for the dedicated B1 plan to warm up. The earlier failed deployment attempts did not
deploy anything to production.

October 27, approximately 11:14 a.m. Central: production PostgreSQL `postgres` was
copied into `campusflow_nonprod`. All 61 application-table row counts matched the
captured source snapshot. Source transactions were read-only and the restricted restore
role passed all production write/create isolation checks. Zero tenant DB overrides
were present. A first restore attempt encountered an Azure platform extension and
rolled back transactionally; the corrected refresh excludes those extensions.
Private source and pre-refresh rollback dumps remain in the protected temporary
`campusflow-prod-to-nonprod-ootr31s9` evidence folder; do not commit or share them.

Post-refresh schema reconciliation: the staging-only `AppTexasCountryCodes` table
remained after `pg_restore --clean`, while production migration history did not contain
its migration. All five columns, the primary and unique indexes, and every seed row
were verified against `20260915032000_AddTexasCountryCodes`; only its already-applied
migration-history record was inserted in non-production. No table data was deleted
or changed. The normal migrator then completed for host and Nelson tenant. Future
refreshes should check staging-only objects before rerunning divergent migrations;
the refresh tool does not perform broad target-table cleanup.

## Launch completion, October 27

- Final staging deployment: `40539547963`, commit
  `cd44235abc86259b784f28edce859e383f2cdd3c`, successful. Development has no Azure
  deployment and no change was promoted to `main` by this work.
- Post-refresh `/health-status`: HTTP 200, Healthy. Maintenance was verified at HTTP
  503 before opening; `MaintenanceMode__Enabled=false` is set only on non-production.
- The public portal reaches Microsoft sign-in with the separate non-production client
  and `https://my-np.nelson.edu/signin-oidc`. The owner approved first-use
  Microsoft consent. Interactive sign-in completed under the Nelson account and loaded
  the dashboard at `/?__tenant=nelson`; sign-out returned through the configured
  non-production callback to `/Account/SignedOut`. No password was requested or copied into the repository.
- Payments and document-tracking writes remain disabled in non-production. Both
  Elements endpoints/credentials remain non-production after the database refresh.
- Small-plan startup was CPU-bound (observed roughly 82–100% during rollout). Set
  `WEBSITES_CONTAINER_START_TIME_LIMIT=600` and `WEBSITE_WARMUP_PATH=/health-status`;
  container filesystem diagnostics are enabled. Cold starts can take several minutes.
- Nine offline restore safety tests pass in addition to the 154 application tests.
- Production database access for this refresh was read-only. No production restore,
  production app-setting change, or production deployment was performed here.
  Shared-server firewall additions were separately and explicitly approved.

## Visible environment identification

`App__EnvironmentLabel` identifies a non-production portal. Any non-empty value
renders a persistent amber banner on all shared portal layouts, the signed-out page,
and the maintenance page. The label is HTML-encoded, and the message states that the
portal is for testing only. It is not dismissible and remains visible while scrolling.

- Non-production Azure setting: `App__EnvironmentLabel=NON-PRODUCTION`.
- Local development: `App:EnvironmentLabel=LOCAL DEVELOPMENT` in the tracked
  `appsettings.Development.json`.
- Production: leave the setting unset or empty (the tracked default). No banner is
  rendered and no production setting needs changing when this code is promoted.
- Other test/training portals can set their own descriptive label using the same key.

This classification belongs in app settings, not in the application database, so a
production-to-non-production database refresh cannot remove the warning.
