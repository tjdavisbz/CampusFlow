# Reviewed Spring 2027 residential import (CF-049)

This is a one-time maintenance command, not a scheduled task. It copies the
reviewed migrated residential assignments from **Elements** into the existing
Nelson CampusFlow housing workspace. Commuter/current assignments and their
metadata remain intact. Both baseline and desired snapshots receive additions;
the job does not queue the additions as new Elements assignments or charges.

The student portal continues to read Elements, never this copied workspace.
`ElementsMigratedHousingReader` reads the still-present Spring 2027 CAMS housing
records and their migration crosswalk directly from the configured Elements
replica. Current StudentLife assignments take precedence over migrated records.
Room and property identities must match; ambiguous/missing mappings stop the
read/import. Other terms retain their existing StudentLife source.

Deploy the lookup fix before importing so Refresh from Elements preserves the
residential roster. Existing live-API room and billing checks still apply to
future housing changes. This import does not undelete StudentLife records or
recreate Elements assignments, and does not waive a conflict or post a charge.

Supply settings securely in `CF_HOUSING_IMPORT_SETTINGS` as a JSON object using
App Service setting names (`ConnectionStrings__Default`,
`ConnectionStrings__ThesisElementsReadOnly`, `App__EnvironmentLabel`). Do not put
credentials in arguments, tracked files, or output. Each environment must use
its own databases; the command checks their identities.

```text
dotnet ResidentialHousingImport.dll production preview /private/path
dotnet ResidentialHousingImport.dll production apply /private/path REVIEWED_PREVIEW_SHA256
```

Use `nonprod` for nonprod. Preview performs read-only database operations and
writes private local evidence. Apply requires the exact reviewed hash of the
source, tenant, workspace, existing JSON and concurrency stamp; a changed source
or workspace stops the transaction. It locks only the existing workspace,
checks recovery is inactive, creates a private backup before writing, preserves
unrelated extra properties/history, updates the two snapshots and records the
source hash and import time. Readback must match before commit. Re-import is
blocked by its audit marker. No schema migration, Elements write, billing write,
automation change, or email is performed.

Backups/source files contain student names and room information. Keep the output
private and outside Git. Unix output files are created with owner-only access.
A failed or uncertain apply must be reconciled by reading the workspace marker
and backup; never blindly retry or restore over a workspace changed by staff.
