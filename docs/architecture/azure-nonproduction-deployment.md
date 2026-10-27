# Azure non-production operations

Updated October 27, 2026 (America/Chicago). Launch work is in progress.
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
A readiness setting blocks deployment until environment setup is verified.

## Azure resources

Subscription `3cc0ce6b-9bac-4bc8-81c2-8b22bd8d5059`, resource group `rg_campusflow`.

| Resource | Configuration |
| --- | --- |
| `asp-campusflow-nonprod-001` | Dedicated Central US Linux B1 plan; additional Azure hosting charges |
| `app-campusflow-nonprod-001` | .NET 10, Staging environment, HTTPS only, TLS 1.2 minimum, FTP disabled, Always On; `/health-status` |
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
  access verified from the local network; 1,512 tables visible. Verify from Azure too.
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
  ownership privileges are checked to be zero. No production credentials are supplied
  to the restore command. No reverse-refresh operation is implemented.
- Non-production maintenance must be enabled. Keep it closed during the refresh.
- A private rollback dump of the previous non-production database is retained before
  a consistent production snapshot is taken. Dumps and diagnostics stay outside Git
  in a private temporary folder and contain sensitive production data.
- Restore uses full TLS validation, no source ownership/ACLs, exit-on-error and a single
  transaction. All source-dump table row counts are compared with the restored target.
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
