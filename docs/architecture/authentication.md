# Authentication

CampusFlow uses Microsoft Entra ID as its only interactive identity provider.

## Identity configuration

The local development registration uses:

- Registration tenant ID: `e640d1df-1720-4922-88fc-d89b2bf9ae17`
- Authority: `https://login.microsoftonline.com/organizations/v2.0`
- Client ID: `4f021040-9fd7-4bd5-bbd0-8d0a2ec95468`
- Redirect URI: `https://localhost:44309/signin-oidc`
- Signed-out callback: `https://localhost:44309/signout-callback-oidc`

The client secret must be stored under `MicrosoftIdentity:ClientSecret` in the ignored `src/CampusFlow.Web/appsettings.secrets.json` file. It must never be committed.

The app registration accepts organizational accounts from any Microsoft Entra tenant. Authentication alone does not grant CampusFlow access; portal-specific record matching provides authorization.

## Student identity linking

Email is used only to locate a student during first-time linking. CampusFlow then stores the immutable Entra tenant and object identifiers (`tid` and `oid`) and uses that pair for later authorization. Ambiguous or missing email matches are rejected.

Admin and Scheduler portals will use separate record-matching policies when their authentication milestones are implemented.

## Student information system boundary

Authentication code does not query a vendor database directly. It uses
`IStudentInformationSystemStudentLookup`, whose implementation is selected from the
tenant's configured student information system provider. Nelson initially uses the
`ThesisElements` provider. Other providers, such as Ellucian, can implement the same
contract without changing the Microsoft sign-in and CampusFlow account-linking flow.

For local development, the Thesis Elements read-only connection belongs in the ignored
`src/CampusFlow.Web/appsettings.secrets.json` file under
`ConnectionStrings:ThesisElementsReadOnly`. The tracked configuration contains only the
provider selection and the connection-string name. Production secrets must come from the
hosting platform's secret store.

The initial Thesis Elements student identity lookup reads
`dbo.CAMS_StudentAddressList_View` and requires one record whose trimmed `Email1` matches
the verified Microsoft email, `ActiveFlag` is `Yes`, and `AddressType` is `Local`.
`StudentUID` becomes CampusFlow's external student identifier. Zero or multiple matches
are denied; CampusFlow does not ask the user to choose or disclose matching records.

## Tenant branding

The UI consumes semantic tenant theme values through `ITenantThemeProvider`; pages do not
contain Nelson-specific color values. `ConfigurationTenantThemeProvider` supplies bootstrap
values during early development. It will be replaced by a database-backed provider when the
admin branding screen is implemented, without requiring dashboard or shell CSS changes.
