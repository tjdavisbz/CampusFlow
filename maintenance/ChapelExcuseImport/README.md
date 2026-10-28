# Chapel excuse request history import

This utility copies a verified, read-only CAMS `SAGU_ChapelExcuseRequests` TSV export
into the new CampusFlow request table. It is separate from the EF migration and
website deployment. Keep the protected export outside Git.

Prerequisites: apply `20261028021912_AddChapelExcuseRequests` to the target database,
verify the Chapel attendance history/mapping is already loaded, and take an
appropriate database backup/snapshot. The utility uses the imported attendance
rows to map each legacy StudentUID to the validated StudentID and student name.

Preview, with no database connection or write:

```sh
dotnet run --project maintenance/ChapelExcuseImport/ChapelExcuseImport.csproj -- \
  --input /secure/path/chapel-excuses.tsv --term 268
```

The supplied October 27 Fall 2026 export previews as 45 rows for 37 students.
One legacy request has an empty reason; the importer preserves it. No request
spans more than three days. The source file is not committed.

Execution requires `CAMPUSFLOW_CHAPEL_POSTGRES` from a secure environment or secret
store. Do not put its value on the command line or in source control. Review the
preview checksum and row count, then specify the exact target host, database,
tenant, and confirmation phrase:

```sh
dotnet run --project maintenance/ChapelExcuseImport/ChapelExcuseImport.csproj -- \
  --input /secure/path/chapel-excuses.tsv --term 268 --execute \
  --sha256 <exact-preview-hash> --count 45 \
  --host <expected-postgres-host> --database <expected-database> \
  --tenant nelson --confirm 'IMPORT CHAPEL EXCUSES 268'
```

The import runs in a serializable transaction and uses a stable source-row hash
for retry detection. It will reject a missing migration or missing/conflicting
student identity. It does not approve requests, change attendance, or send mail.
Run and verify separately for non-production and production only when each
environment is approved for the migration.
