# Azure non-production preparation

Prepared October 24, 2026, approximately 9:10–9:20 a.m. America/Chicago.
Status: infrastructure prepared; application not deployed; paused at the owner's request
until the non-production Elements API account is available.

## Resources created

Subscription: `3cc0ce6b-9bac-4bc8-81c2-8b22bd8d5059`; resource group: `rg_campusflow`.

| Resource | Configuration |
| --- | --- |
| `asp-campusflow-nonprod-001` | Dedicated Linux B1 plan, Central US; additional Azure charges |
| `app-campusflow-nonprod-001` | .NET 10, HTTPS only, minimum TLS 1.2, FTP disabled, Always On |
| `campusflow_nonprod` | New empty PostgreSQL database on existing `psql-campusflow-prod-001`; shares database-server capacity |
| `id-github-campusflow-nonprod-001` | Dedicated deployment identity; Website Contributor scoped to the new app only |
| GitHub `nonproduction` environment | Three Azure identity secrets configured; GitHub environment OIDC federation |

App system-assigned managed identity is enabled. Health-check path is `/health-status`.
The platform resource is running; that does not establish application health because no
CampusFlow package has been deployed.

## Application settings

All new settings had no prior value on this newly created app.

- `ASPNETCORE_ENVIRONMENT=Staging`.
- `App__SelfUrl` and `AuthServer__Authority` initially use
  `https://app-campusflow-nonprod-001.azurewebsites.net`.
- Intended final public URL: `https://test.my.nelson.edu`.
- `ConnectionStrings__ThesisElementsReadOnly` uses server
  `sql-elm-nonprod-usc-replica.database.windows.net`, database `EL-NP-NEL-DB`,
  and the existing production replica credentials, reused privately as instructed.
  No credential values are included here. SQL connectivity has not been tested.
- Elements API `TenantHost=el-np-nel.thesiscloud.com`, as supplied by the owner.
- Elements API `BaseUrl` and `RegistrationBaseUrl` are explicitly empty to override
  the production defaults in tracked application configuration.
- `Payments__Payflow__Enabled=false`; `Payments__Payflow__TestMode=true`.
- `BillApproval__DocumentTracking__Enabled=false`.
- `MaintenanceMode__Enabled=true`, with a setup-in-progress message.
- `CampusFlow__NonProductionReady=false`; the deployment workflow refuses to deploy
  until this is explicitly changed after the environment is verified.

No production app settings, production maintenance settings, DNS records, TLS bindings,
or existing database data were changed.

## Deployment workflow

`.github/workflows/deploy-nonproduction.yml` is maintained on `develop`.
It deploys only from `staging`, uses the `nonproduction` GitHub environment, builds and tests,
requires its own OpenIddict certificate secret, checks the readiness flag, deploys only
to the new app, and retries the deployed `/health-status` check.
The owner authorized committing and pushing this preparation to `develop` on October 24,
2026. No staging promotion or deployment is authorized in this phase. YAML syntax was
parsed successfully. Work is paused until the non-production Elements account and
confirmed Integration API URL are available.

## Branch promotion and local development

The intended promotion path is local development → `develop` → `staging` → `main`.

| Stage | Deployment | Data and integrations |
| --- | --- | --- |
| Local development | Local application only | Separate local CampusFlow PostgreSQL database; Elements non-production read-only replica is the tentative shared source |
| `develop` | No Azure site deployment | Integration branch; can run build/test checks without deployment |
| `staging` | Non-production Azure app at `test.my.nelson.edu` once DNS/TLS is configured | `campusflow_nonprod`, non-production Elements replica and APIs |
| `main` | Existing production Azure app at `my.nelson.edu` | Production application database and Elements integrations |

Promote changes with reviewed pull requests from `develop` into `staging`, then from
`staging` into `main` after validation. A Git pull only updates a local checkout;
the merge/push to each remote deployment branch triggers its deployment. Fix failed
staging validation in development and repeat promotion. Carry urgent production fixes
back into `staging` and `develop` so branches do not diverge.

Keep local credentials in ignored secrets or user secrets and Azure credentials in the
corresponding environment settings. Local and staging must have separate writable
CampusFlow databases. Shared non-production Elements data means API writes from local
work can affect staging tests; keep local writes/payments disabled until an explicit
test-data and account plan exists. A dedicated Elements sandbox or refreshed replica
would provide better isolation if available later. No local connection settings have
been changed by this preparation.

The non-production workflow has both a push trigger and a job condition restricted to
`staging`; manual invocation on another branch cannot deploy. Its `slot-name: production`
means the default slot of the **non-production app**, not the production CampusFlow app.
The existing production workflow remains unchanged. Branch protections and GitHub
environment deployment-branch restrictions have not yet been configured or audited.

## Remaining work before deployment

1. Confirm the Integration API URL and registration gateway for non-production, then
   provision/verify the non-production Elements API account. Same account details are
   intended, but the owner cannot create the account yet.
   `https://app-elm-nonprod-usc-integrationapi.azurewebsites.net` is an inferred candidate:
   public DNS resolves and unauthenticated HTTPS returns 404. This does not verify the API,
   its tenant routing, permissions, or that it is the correct environment. Do not configure
   it on this evidence alone. The confirmed Elements web hostname resolves publicly.
2. Verify replica SQL login, SELECT access, and the necessary firewall rules from the
   new app's outbound network. Do not broaden access to all internet addresses.
3. Create a dedicated PostgreSQL role restricted to `campusflow_nonprod`, configure the
   app's `Default` connection, and run the CampusFlow migrator against that database only.
   Do not copy production student data or point this app at the production application DB.
4. Create a separate non-production OpenIddict signing/encryption PFX; store its base64
   value as GitHub environment secret `OPENIDDICT_PFX_BASE64` and its passphrase in the
   new app's `AuthServer__CertificatePassPhrase`. Never commit certificate material.
5. Configure non-production Entra authentication and callbacks for the final hostname.
   Prefer a separate app registration and credential; preserve production callbacks.
6. Add `test` CNAME in the delegated public `my.nelson.edu` zone pointing to
   `app-campusflow-nonprod-001.azurewebsites.net.` and `asuid.test` TXT using this
   new app's `customDomainVerificationId`. Preserve all existing root, mail, and
   subordinate records. Bind the custom hostname and issue/bind its managed TLS
   certificate after verifying issuance prerequisites. The existing `*.nelson.edu`
   certificate does not cover `test.my.nelson.edu`. Public DNS currently has no result
   for the requested test hostname. Check Nelson split DNS as well.
7. Once DNS/TLS is ready, change the two app URL settings to
   `https://test.my.nelson.edu` and configure `/signin-oidc` and
   `/signout-callback-oidc` in Entra. Validate login/logout and cookies.
8. Review and commit/push the workflow on `develop` without deploying. Promote it to
   `staging` through a reviewed pull request. Explicitly set readiness only after all
   dependencies are isolated, then monitor the staging deployment and verify health,
   maintenance, static assets, DNS, TLS, authentication, and non-production data access.
9. Explicitly decide when to open the non-production portal. Production maintenance
   must remain enabled until the owner separately authorizes production reopening.

Production observations requiring separate follow-up: its two canonical URL settings still
reference its Azure hostname and its App Service health-check path is unset. These were
observed read-only and left unchanged.
