# Azure production deployment

CampusFlow deploys to the production Azure App Service when a commit reaches `main`.
The workflow is `.github/workflows/deploy-production.yml`. It restores dependencies, runs
the test suite, publishes `CampusFlow.Web`, authenticates to Azure through GitHub OpenID
Connect, and deploys the published output to `app-campusflow-prod-001`.

Database migrations are deliberately not part of the web deployment. The production
PostgreSQL firewall does not admit arbitrary GitHub-hosted runner addresses, and schema
changes deserve a controlled migration step. Run `CampusFlow.DbMigrator` from an approved
network location before deploying code that requires a new migration.

## GitHub production environment

Create a GitHub environment named `production`. Configure these environment secrets:

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Client ID of the Azure user-assigned identity trusted by GitHub |
| `AZURE_TENANT_ID` | Nelson Microsoft Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | CampusFlow Azure subscription ID |
| `OPENIDDICT_PFX_BASE64` | Base64 representation of the production OpenIddict PFX |

The PFX password is not a GitHub deployment secret. Configure it in the App Service as
`AuthServer__CertificatePassPhrase`. The certificate and password must be generated for
production and must not reuse the development values in tracked configuration.

The `production` environment can require approval and restrict deployment branches to
`main`. Because the workflow job targets this environment, the Azure federated credential
must also target the `production` GitHub environment rather than a branch subject.

## Azure deployment identity

Use a dedicated user-assigned managed identity for GitHub deployment. Add a federated
credential with these values:

- Organization: `tjdavisbz`
- Repository: `CampusFlow`
- Entity: Environment
- Environment: `production`

Grant that identity the `Website Contributor` role scoped to the production Web App. It
does not need subscription-wide ownership and must not receive access to application
secrets or the production database merely to deploy the site.

## App Service configuration

Runtime configuration and secrets belong in App Service settings or Key Vault references,
not in the workflow or repository. At minimum, production must override the public app and
issuer URLs, the PostgreSQL connection string, the Microsoft identity client secret, the
Elements read/API credentials, the Payflow credentials, and the OpenIddict certificate
password. Production must also remove the development-only Elements current-term override.

The App Service health-check path is `/health-status`. Enable the App Service health check
after the first successful deployment and verify that the application starts with the
production settings before directing the custom domain to it.
