# Non-production Elements isolation check

Checked October 28, 2026 against the running `app-campusflow-nonprod-001` Azure App Service. No credentials or tokens are recorded here.

| Effective setting | Observed non-production destination |
| --- | --- |
| ASP.NET environment | `Staging` |
| Elements integration API | `https://app-elm-nonprod-usc-integrationapi.azurewebsites.net` |
| Elements registration gateway | `https://np-usc-gateway.thesiscloud.com` |
| Elements tenant host | `el-np-nel.thesiscloud.com` |
| Elements read-only SQL replica | `sql-elm-nonprod-usc-replica.database.windows.net` / `EL-NP-NEL-DB` |
| CampusFlow application database | `campusflow_nonprod` on the shared `psql-campusflow-prod-001` server |
| Payflow | Disabled; test mode configured |
| Bill Approval document tracking | Disabled |

The non-production API credential settings are present. A read-only `GET /api/Login/Authenticate` request was sent only after checking the exact non-production API and tenant hosts; it returned HTTP 200 and an access token. The token was not printed or saved. The application code's Elements HTTP calls use the configured integration API or registration gateway; Elements SQL readers use `ThesisElementsReadOnly`. No hard-coded production Elements call was found in the reviewed application source. The tracked `appsettings.json` still contains production Elements defaults, which would have been used if a non-production override disappeared.

This change adds a startup guard for the `Staging` environment. It requires the exact approved non-production integration API, registration gateway, tenant host, replica server, and replica database. If any value falls back to production or is missing, the portal refuses to start before its application services run. The guard does not change Production or Development. Six focused tests cover the approved settings and each production fallback.

This is a configuration and source-code check, not a historical outbound-traffic audit. The App Service has no Azure diagnostic settings configured for dependency/egress logs, and its performance logger records only slow dependencies. Those sources cannot prove that no production call occurred in the past. The guard is on branch `codex/nonprod-elements-egress-guard`; it takes effect only after that code is deployed to non-production. Before deployment, compare the live non-production build with the chosen source revision to avoid replacing unrelated features. After deployment, verify `/health-status` and repeat the effective-setting check.
