# Tenant and portal resolution

CampusFlow resolves two independent pieces of request context:

1. The university tenant, such as `nelson`.
2. The portal experience: Student, Admin, or Advisor.

Production will resolve both values from an approved hostname mapping. Until custom DNS and Azure hosting are available, local development uses query parameters:

- `https://localhost:44309/?tenant=nelson&portal=student`
- `https://localhost:44309/?tenant=nelson&portal=admin`
- `https://localhost:44309/?tenant=nelson&portal=advisor`

The legacy `portal=scheduler` development value remains an alias for `advisor` while links
and terminology are transitioned.

The middleware translates `tenant` to ABP's standard `__tenant` resolver input and records the portal context. This behavior runs only when the ASP.NET Core environment is `Development`; deployed environments must use registered hostnames.

Nelson is seeded as the initial tenant. The query-string mechanism is temporary development infrastructure, not a production tenant-selection mechanism.
