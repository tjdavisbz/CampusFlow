# Portal entry and role switching

CampusFlow uses one tenant-facing entry point, such as `portal.nelson.edu`, for Student,
Admin, and Advisor experiences. Microsoft Entra ID provides one interactive sign-in. The
application then resolves the signed-in person's authorized CampusFlow portal roles.

## Entry behavior

- A person with exactly one authorized portal role is redirected directly to that area.
- A person with multiple roles sees a portal chooser after sign-in.
- Multi-role users receive a persistent **Switch portal** control inside the application.
- The most recently used authorized portal may be remembered as a convenience, but an
  explicit deep link always wins.
- Authentication never grants a portal role by itself. Student access requires a linked
  student profile; Advisor access requires a verified Elements `CAMSUser` match and an
  applicable assignment; Admin access requires an explicit CampusFlow permission or role.

## URL model

The hostname remains stable while paths make the active security and navigation context
explicit:

- `https://portal.nelson.edu/student`
- `https://portal.nelson.edu/admin`
- `https://portal.nelson.edu/advisor`

Explicit paths support bookmarks, deep links, support diagnostics, and safe authorization
checks. The portal switcher changes the path but does not require another login or hostname.
The three portals remain separate application areas with independent authorization policies,
menus, landing pages, and workflow services even when deployed as one web application.

## Local development

The existing development query remains valid and uses the trusted localhost certificate:

- `https://localhost:44309/?tenant=nelson&portal=student`
- `https://localhost:44309/?tenant=nelson&portal=admin`
- `https://localhost:44309/?tenant=nelson&portal=advisor`

CampusFlow should add equivalent development paths (`/student`, `/admin`, `/advisor`) so
normal testing does not depend on query strings. Subdomains such as `student.localhost` are
not required and can introduce avoidable development-certificate and redirect-URI setup.

## Authorization boundary

Portal selection is user experience state, not authorization. Every page and application
operation must independently require its portal permission. A user cannot gain Advisor or
Admin access by changing a path, query string, cookie, or remembered portal preference.
