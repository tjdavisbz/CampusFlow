using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace CampusFlow.LegacyRecoveryImport;

internal static class PrivateManifestFile
{
    internal static string OutputPath(string input)
    {
        if (!Path.IsPathFullyQualified(input)) throw new ImportFailure("AbsolutePrivatePathRequired");
        var full = Path.GetFullPath(input); var parent = Path.GetDirectoryName(full) ?? throw new ImportFailure("PrivatePathRequired");
        var cursor = Path.GetPathRoot(full)!;
        foreach (var component in parent[cursor.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, component);
            if (Directory.Exists(cursor) && new DirectoryInfo(cursor).LinkTarget is not null) throw new ImportFailure("SymlinkOutputNotAllowed");
            if (Directory.Exists(Path.Combine(cursor, ".git")) || File.Exists(Path.Combine(cursor, ".git")) ||
                File.Exists(Path.Combine(cursor, "HEAD")) && Directory.Exists(Path.Combine(cursor, "objects")) && Directory.Exists(Path.Combine(cursor, "refs"))) throw new ImportFailure("OutputMustBeOutsideGit");
        }
        return full;
    }
    internal static string Write(string input, byte[] bytes)
    {
        var full = OutputPath(input); var parent = Path.GetDirectoryName(full)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(parent);
        else
        {
            Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(parent) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new ImportFailure("PrivateOutputDirectoryRequired");
        }
        var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(full, streamOptions))
        {
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User ?? throw new ImportFailure("PrivateFileOwnerUnavailable");
                var security = new FileSecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(user);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                new FileInfo(full).SetAccessControl(security);
            }
            else File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            stream.Write(bytes); stream.Flush(true);
        }
        EnsurePrivate(full);
        return ImportModel.Sha(bytes);
    }
    internal static byte[] Read(string input, string expectedHash)
    {
        var full = OutputPath(input); EnsurePrivate(full);
        if (new FileInfo(full).LinkTarget is not null || new FileInfo(full).Length is <= 0 or > 134217728) throw new ImportFailure("InvalidPrivateManifest");
        var bytes = File.ReadAllBytes(full);
        if (!ImportModel.IsHash(expectedHash) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
            throw new ImportFailure("ManifestHashMismatch");
        RejectDuplicateProperties(bytes);
        return bytes;
    }
    private static void EnsurePrivate(string full)
    {
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User ?? throw new ImportFailure("PrivateFileOwnerUnavailable");
            var acl = new FileInfo(full).GetAccessControl();
            if (!acl.AreAccessRulesProtected) throw new ImportFailure("PrivateFilePermissionsRequired");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))
                    throw new ImportFailure("PrivateFilePermissionsRequired");
        }
        else if ((File.GetUnixFileMode(full) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new ImportFailure("PrivateFilePermissionsRequired");
    }
    internal static void RejectDuplicateProperties(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes); var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!))
                throw new ImportFailure("DuplicateManifestProperty");
        }
    }
}
