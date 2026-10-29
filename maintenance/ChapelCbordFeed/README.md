# CBORD SFTP archive and attendance job

This is a scheduled WebJob for the production CampusFlow Linux App Service.
It downloads the one expected `ChapelAttendanceDaily.csv` file, verifies the pinned CBORD
Ed25519 host key, strictly parses every scan, and stores the exact file bytes in
`AppChapelFeedFiles`. The table has a tenant-and-checksum unique key, so repeat runs do not
duplicate a file. It never deletes or renames remote files. After archiving, the optional
attendance step classifies only pending scheduled/legacy-import rows. Existing staff
corrections, approved excuses, and prior CBORD decisions are preserved. The archive is
protected student data and must not be downloaded to Git or logged.

The WebJob schedule is `0 30 18,19 * * 3-5` in UTC. Both 18:30 and 19:30 UTC are invoked
on Wednesday–Friday; the process checks `America/Chicago` time and fetches only on the
invocation that occurs at 1:30 p.m. Central. This handles daylight-saving time without
changing the web app's time zone. `--manual` is available for an operator-triggered run.

Production configuration:

- `ChapelSftp__Password`: CBORD password, configured in App Service or as a Key Vault reference.
  The literal `REPLACE_WITH_CBORD_PASSWORD` is rejected.
- `ChapelFeed__Enabled=true`: enable the archive job only after the migration, credential,
  Kudu WebJob setting, and remote listing have been verified. Default is disabled.
- `ChapelFeed__ApplyAttendance=true` and `ChapelFeed__ApplyFromLocal=2026-10-29`: enable
  attendance processing **only in production**. The code additionally checks the exact
  production App Service name, PostgreSQL host, and database before any write. Do not
  configure either setting on non-production.
- `ChapelFeed__ExpectedHost=psql-campusflow-prod-001.postgres.database.azure.com` and
  `ChapelFeed__ExpectedDatabase=postgres`: fail closed if the job targets another database.
- The existing App Service PostgreSQL `Default` connection is read from
  `POSTGRESQLCONNSTR_Default` (or `ConnectionStrings__Default` for local testing).
- `WEBSITE_SKIP_RUNNING_KUDUAGENT=false` and Always On are required for Linux scheduled WebJobs.

Preview a local sample without SFTP or PostgreSQL:

```sh
dotnet run --project maintenance/ChapelCbordFeed/ChapelCbordFeed.csproj -- \
  --preview-file /secure/path/ChapelAttendanceDaily.csv
```

The job fails closed for missing/extra CSV filenames, an unexpected header, malformed
rows, an empty file, oversized content, an unexpected SSH host key, or an incorrect database.
The attendance step also requires a complete roster, at least 300 matched scan IDs per
event, and a configured event end time. It marks pending rows Present for a valid scan-in/out
pair, Absent for late/scan-in-only/no qualifying pair, and leaves ambiguous cases pending for
staff review. It waits until an hour after the event ends. A file retry is idempotent.
Exceptions should be investigated in WebJob run history. Successful output contains only
aggregate counts and the file checksum, never student IDs.

The packaged schedule currently covers **Fall 2026 only**. Before the next term, import and
verify its Chapel roster and schedule and extend this job's schedule data; a pending event
outside the configured schedule causes a fail-closed error rather than a silent import.
Recurring excuses already marked Excused and manual decisions remain protected. Any future
bonus-credit automation requires a separate reviewed workflow.
