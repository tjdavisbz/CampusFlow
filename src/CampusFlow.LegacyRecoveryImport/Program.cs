using System.Text.Json;
using CampusFlow.LegacyRecoveryImport;

// Connection strings are accepted only through process environment variables.
// Never echo arguments, provider exceptions, student identifiers or verification data.
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("""
        CampusFlow legacy recovery verification import — no Microsoft/AD password operations.
        export --environment nonprod|production --expected-legacy-server HOST --expected-legacy-database DB
               (--student-id INSTITUTIONAL_ID | --all) --output ABSOLUTE_PRIVATE_PATH
               [--reuse-manifest ABSOLUTE_PRIVATE_PATH --reuse-sha256 SHA256]
        prepare-xml --environment nonprod|production --expected-legacy-server HOST --expected-legacy-database DB
                    --input SOURCE_XML --expected-sha256 SHA256 --output ABSOLUTE_PRIVATE_PATH
                    (--student-upn STUDENT_UPN | --all) [--reuse-manifest PATH --reuse-sha256 SHA256]
        XML preparation never writes a database; nonprod requires one explicitly selected student.
        apply  --environment nonprod|production --expected-legacy-server HOST --expected-legacy-database DB
               --input ABSOLUTE_PRIVATE_PATH --expected-sha256 SHA256 --expected-rows COUNT [--apply]
        Export defaults to one explicitly selected institutional StudentID. Bulk requires --all.
        Optional paired reuse flags preserve unchanged pilot proofs after fresh source-answer verification.
        Apply is a read-only preview unless --apply is explicitly supplied; existing proofs are never overwritten.
        Environment variables: CAMPUSFLOW_RECOVERY_LEGACY_CONNECTION_STRING (export only),
          CAMPUSFLOW_RECOVERY_ELEMENTS_CONNECTION_STRING, CAMPUSFLOW_RECOVERY_PORTAL_CONNECTION_STRING (apply only).
        Connections must match the selected environment, use trusted TLS, and use read-only credentials for both SQL sources.
        Output must use a real absolute path outside Git in a private directory (symbolic-link directories are rejected).
        Unix files are created mode 0600; Windows files receive owner-only ACLs.
        No SSNs, birthdates, portal passwords or plaintext security answers are exported or printed.
        No export/import is performed by building this project. Runtime recovery remains disabled until separately enabled.
        """);
    return 0;
}
try
{
    if (args.Length > 0 && args[0] == "prepare-xml")
    {
        Console.WriteLine(JsonSerializer.Serialize(await XmlSourcePreparation.RunAsync(XmlPreparationOptions.Parse(args))));
        return 0;
    }
    var options = ImportOptions.Parse(args);
    var result = options.Mode == "export" ? await SqlSources.ExportAsync(options) : await PortalImport.RunAsync(options);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (ImportFailure failure) { Console.Error.WriteLine("Recovery verification operation stopped: " + failure.Code + ". No account/password operation was attempted."); return 2; }
catch (Exception) { Console.Error.WriteLine("Recovery verification operation failed. No account/password operation was attempted; no sensitive provider details were logged."); return 2; }
