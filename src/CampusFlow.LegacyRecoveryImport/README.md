# Legacy account-recovery verification import

This is an offline, operator-run data migration utility. Building it, displaying help or running its tests does not export/import real student data, change any password or enable the public recovery feature. It never invokes AD or Microsoft Graph.

The public recovery page uses a uniquely matched current Elements student and separately imported verification proof. It does not connect to legacy CAMS during a request.

## Source and mapping

The supplied legacy portal names `windb3` / `CAMS_Enterprise`. This is configuration evidence, not proof that the server is reachable. Use a reviewed source for the selected environment and trusted TLS; the utility rejects `Encrypt=False` and `TrustServerCertificate=True`.

Export matches legacy and current students using the **institutional StudentID**, retaining both independent StudentUIDs. It never assumes UIDs survived migration unchanged. The current account must be uniquely mapped from active Local Email1 to a `@my.nelson.edu` address; the email is not exported. Missing or ambiguous mappings are skipped and counted. A student with no reliable legacy mapping has no usable imported proof and must use an IT-approved recovery path.

For a mapped student, export reads at most two legacy security-question rows. Duplicate questions, invalid questions, absent answers or unavailable eligibility produce blocked proof. With zero questions only, it checks that `dbo.FERPA` exists and exports the boolean result of the exact legacy predicate `dbo.FERPA(uid) <> -9999999`. The raw function result is never exported or interpreted as another field.

Answers are hashed in memory using the shared `RecoveryAnswerHash` policy: trim, Unicode Form C, uppercase invariant, then salted PBKDF2-SHA256 with 600,000 iterations. The source database and Answer-column collations are captured in the private manifest. Only these explicitly reviewed case-insensitive, accent-sensitive variants are accepted:

- `SQL_Latin1_General_CP1_CI_AS`
- `Latin1_General_CI_AS`
- `Latin1_General_100_CI_AS`
- `Latin1_General_100_CI_AS_SC`

Other collations—including case-sensitive, accent-insensitive and unreviewed locale-specific rules—stop export for review. Normalization approximates the reviewed legacy case-insensitive comparison; it is not an arbitrary SQL-collation emulator. No plaintext answers, SSNs, birthdates, portal passwords or Microsoft passwords are exported or printed. SQL credentials are never accepted as command-line arguments or written to the artifact.

## Private configuration and export

Supply connection strings only through an approved process-environment secret mechanism:

- `CAMPUSFLOW_RECOVERY_LEGACY_CONNECTION_STRING`: read-only legacy source; export only.
- `CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING`: matching read-only Elements replica; export and apply.
- `CAMPUSFLOW_RECOVERY_PORTAL_CONNECTION_STRING`: matching CampusFlow PostgreSQL database; apply only.

Do not put credentials in source, command history, transcripts or repository files. The environment selects fixed Elements/portal server and database pins. Legacy server/database pins are required explicitly. Do not bulk copy production verification data into nonprod; start with the one explicitly approved test student's institutional StudentID.

First inspect help:

```text
dotnet run --project src/CampusFlow.LegacyRecoveryImport -- --help
```

For a deliberately authorized export, use `export`, select `--environment`, provide `--expected-legacy-server` and `--expected-legacy-database`, and provide exactly one of:

- `--student-id`: one explicit institutional StudentID (not a StudentUID or email).
- `--all`: a separately authorized bulk export.

Also supply `--output` as an absolute, real path outside Git in a private directory. Symbolic-link directories are rejected. Output is create-new only: existing files are never overwritten. On Unix the file mode is `0600`; on Windows an owner-only ACL is applied before verification content is written. The output contains salted answer hashes and identifiers and remains confidential. Do not attach it to email, paste it into chat or commit it.

Export prints only sanitized counts and the artifact SHA256. Record the reviewed hash and row count separately. Preserve this exact artifact for an idempotent retry. To expand a reviewed pilot into a later bulk export, supply the paired export-only options `--reuse-manifest ABSOLUTE_PRIVATE_PATH --reuse-sha256 SHA256`. The prior artifact must pass the same private-file/hash, environment, source/collation, mapping and row-fingerprint checks. For unchanged mapped questions and flags, the tool reuses the previous salted hash only after verifying the freshly read source answer in memory against it. Unchanged no-question or blocked proofs are also preserved. The new artifact contains those exact prior proofs plus newly mapped students, so insert-only apply remains idempotent.

A changed prior mapping, question, answer, eligibility flag, blocked status or omitted prior record stops export for explicit review; no prior proof is silently dropped, overwritten or skipped. The same fingerprint context, including source-name casing and collation metadata, is required. Do not regenerate an already imported pilot without the paired reuse flags: new random salts would require deliberate conflict review.

## Preview and insert-only apply

The reviewed account-recovery migration must already exist. The utility does not create tables, seed accounts, grant permissions or enable feature flags.

Use `apply` with `--environment`, the same explicit legacy source pins, `--input`, `--expected-sha256` and `--expected-rows`. Without the additional **`--apply`** switch, this is a read-only preview and rolls its transaction back.

Before an insert, the utility verifies:

- Exact artifact hash, format, environment, tenant name, source metadata and canonical per-row fingerprints.
- Unique current/legacy mappings and a fresh current Elements StudentID-hash/active-Local-email check.
- The matching PostgreSQL host/database, trusted certificate verification and unique Nelson tenant.
- The reviewed table and unique `(TenantId, StudentUid)` index.
- Every existing proof matches exactly. A changed, blocked or differently hashed existing proof stops the operation; it is never overwritten automatically.

Only missing rows in `AppAccountRecoveryVerifications` may be inserted in one transaction. No request-log records, student records, SQL users, AD/Microsoft accounts or passwords are changed. Preserve imported audit history and the private reviewed artifact. Separately review import results and obtain authorization before enabling runtime recovery or testing a real Microsoft password change.

## Offline verification

```text
dotnet test src/CampusFlow.LegacyRecoveryImport/Tests/CampusFlow.LegacyRecoveryImport.Tests.csproj
```

Tests use synthetic values and temporary private files only. Unix file protections are tested on the current platform; Windows ACL support is compiled but still requires a controlled Windows operator check before use there.

## SQL-only source export for an internal SSMS operator

At TJ's request, `sql/Export-CF028-Legacy-Recovery.sql` provides a standalone read-only source export. It requires no PowerShell, CampusFlow checkout, .NET installation or Elements connection on the internal machine. Use an existing authorized SSMS connection to `windb3`, run the entire script in `CAMS_Enterprise`, and leave `@StudentID = NULL` for all students or provide one institutional StudentID.

This is a deliberate alternative to the hashed C# export above: **the SQL-only XML contains readable saved security answers**. It contains no SSNs, birthdates, names, portal passwords or SQL/Microsoft credentials. Save it in a private, access-controlled location outside Git; do not email it or paste its content into chat. Give the importing operator a local path after transferring it through an approved protected method. Raw source exports must never be committed or used directly by runtime recovery.

In SSMS, select Results to Grid. Before running, set Query > Query Options > Results > Grid > Maximum Characters Retrieved > XML data to Unlimited. Click the single XML result link and save the opened XML document as `CF028-legacy-recovery.xml`. Save the XML document itself, not a screenshot, CSV or copied grid cell. See Microsoft's [FOR XML documentation](https://learn.microsoft.com/en-us/sql/relational-databases/xml/for-xml-sql-server) for the typed XML result format.

The script exports the legacy StudentUID and institutional StudentID for mapping, question count, up to two saved question rows, the exact no-question eligibility boolean and source/collation metadata. More than one saved question remains ambiguous and will be blocked; selecting two rows does not turn duplicate records into a usable single question. If the scalar `dbo.FERPA` function is absent, no-question records are explicitly ineligible. The raw function return value is never exported. The script stops if the required tables/metadata are unavailable or the selection is empty/over100000 students. Its dynamic SQL substitutes only one of two fixed eligibility expressions; the optional StudentID remains a typed parameter.

The XML is a **source snapshot, not a ready-to-apply import manifest**. After receipt, validate its complete XML structure, exact source identity, collation and expected student count; preserve leading zeroes in StudentID. Parse with external entities/DTDs prohibited. Match current Elements students uniquely and hash valid answers in memory with the existing shared `RecoveryAnswerHash` before creating the environment-bound manifest. Ambiguous/missing proof must fail closed; raw answers must not be carried into the manifest, logs or CampusFlow database. Do not bulk-import the legacy source into nonprod; the approved personal test account is the initial nonprod scope.

Verification performed locally: Microsoft ScriptDom parsed the outer batches and both dynamic SELECT variants using SQL Server2008 grammar, with no table/data/permission mutations found. No live CAMS execution was performed; actual source availability, schema and permissions will be checked when the operator runs it.

## Prepare a hashed manifest from the received SQL XML

The `prepare-xml` command validates and converts the SQL source snapshot without connecting to legacy CAMS and without writing any database:

```text
prepare-xml --environment nonprod|production
  --expected-legacy-server windb3 --expected-legacy-database CAMs_Enterprise
  --input ABSOLUTE_SOURCE_XML_PATH --expected-sha256 REVIEWED_SOURCE_SHA256
  --output ABSOLUTE_PRIVATE_MANIFEST_PATH
  (--student-upn SELECTED_STUDENT_UPN | --all)
```

It accepts the saved XML document or the exact SSMS `(No column name)` prefix. The complete document, supported source format, unique metadata, source/collation pins, declared student count and question counts must validate before mapping. DTDs, external entities, unknown structures, duplicate legacy UIDs and malformed/truncated data are rejected without logging personal values.

This preparation command requires only `CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING` for the selected read-only replica. It never uses the portal connection or calls Graph. Nonprod requires one explicit student UPN; `--all` is rejected there. Production bulk preparation must explicitly select `--all`.

The current target account follows the existing portal's **active Local Email1** mapping from `CAMS_StudentAddressList_View`. A student needs exactly one active Local row, a valid `@my.nelson.edu` UPN, and exclusive ownership of that email across all such rows. `Student.InstitutionalEmail` is not populated in the inspected replicas and is not used for recovery. The online identity verifier and offline mapper use the same source and ownership rules. The bulk reader scans students and active Local addresses separately, preserving every StudentID occurrence for ambiguity checks while avoiding a correlated account lookup per student. Missing or ambiguous mappings are counted and skipped, never guessed.

The converter hashes valid answers in memory, writes only normal environment-bound proof records, and produces an owner-only, create-new manifest outside Git. It uses the existing PBKDF2 policy and at most two hashing workers. The optional paired `--reuse-manifest` / `--reuse-sha256` flags preserve unchanged previously reviewed salts only after checking the current source answers; changed/omitted prior proof stops preparation. Existing insert-only preview/apply rules remain unchanged.

Preparing this manifest does not set `VerificationImportReady`, change a password, create a table or import a record. Actual import and runtime enablement are separate steps. Runtime replica configuration must also enforce trusted TLS; overriding a private worker's in-memory TLS flags does not change either portal's settings.
