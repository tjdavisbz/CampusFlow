# One-time chapel history import

This utility imports a **fresh, read-only CAMS export** into CampusFlow's PostgreSQL
`AppChapelAttendanceRecords` table for one term. The source export is protected student
data and must stay outside Git. It is not part of an EF migration and is not run by
website deployment. Run it separately for non-production and production, using the
approved database access path for each environment.

Prerequisites:

1. Apply `20261027233041_AddChapelAttendanceFoundation` using the normal, controlled
   `CampusFlow.DbMigrator` step before deploying code that depends on these tables.
2. Produce the history export with
   [`docs/discovery/chapel-fall-2026-history-export.sql`](../../docs/discovery/chapel-fall-2026-history-export.sql)
   and a same-term schedule export. Save both outside the repository.
3. Make an appropriate target PostgreSQL backup/snapshot according to the operations
   runbook. This import is transactional, but a backup remains the recovery path after commit.
4. Have read-only Elements SQL access and authorized PostgreSQL access. The utility
   rechecks every StudentID/StudentUID pair against Elements immediately before writing.

Preview (no database connection or writes):

```sh
dotnet run --project maintenance/ChapelHistoryImport/ChapelHistoryImport.csproj -- \
  --history /secure/path/chapel-history.tsv \
  --schedule /secure/path/chapel-schedule.tsv \
  --term 268
```

Review the row/event/student counts, status counts, conflict count, and SHA-256.
For the October 27 Fall 2026 snapshot, the preview found 53,000 attendance rows,
53 chapel events, 1,000 students, and two legacy rows with conflicting status flags.
The effective status follows the old student portal precedence (Present, then Absent,
then Excused), while the original three flags are retained in `ExtraProperties`.

Execution requires all of the following explicit checks. Set the connection strings
through a secure environment or secret store, **not** on the command line or in Git:

- `CAMPUSFLOW_CHAPEL_ELEMENTS_READONLY`: read-only Elements SQL Server connection.
- `CAMPUSFLOW_CHAPEL_POSTGRES`: destination CampusFlow PostgreSQL connection.

```sh
dotnet run --project maintenance/ChapelHistoryImport/ChapelHistoryImport.csproj -- \
  --history /secure/path/chapel-history.tsv \
  --schedule /secure/path/chapel-schedule.tsv \
  --term 268 \
  --execute \
  --sha256 <the-exact-preview-hash> \
  --count <the-exact-preview-row-count> \
  --host <expected-postgres-hostname> \
  --database <expected-postgres-database-name> \
  --tenant nelson \
  --confirm 'IMPORT CHAPEL HISTORY 268'
```

The utility validates the PostgreSQL host, database name, tenant, migration, current Elements identity
mapping, and all staged records. It locks the term, inserts in one transaction, then
reconciles the complete destination set before committing. If the exact import already
exists, it makes no application-data change. A partial or conflicting existing set
aborts without changing application data; investigate before retrying. The original
legacy attendance IDs are retained for reconciliation and rollback planning.

Do not reuse an old preview hash for a fresh export. Re-export and preview shortly
before each environment's one-time import. Importing the data does **not** deploy the
website or configure future CBORD SFTP ingestion.
