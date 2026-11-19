using System;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.AccountRecovery;

internal sealed class WritebackRecoverySettings(IConfiguration config)
{
    internal const string Prefix = "PasswordRecovery:";
    internal static readonly string[] Scopes = ["https://graph.microsoft.com/User.Read.All", "https://graph.microsoft.com/UserAuthenticationMethod.ReadWrite.All"];
    internal string Environment => string.Equals(config["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ? "nonprod" : "production";
    internal Guid ClientId => Guid.TryParse(config[Prefix + "DelegatedClientId"], out var id) ? id : Guid.Empty;
    internal Guid ServiceId => Guid.TryParse(config[Prefix + "ServiceAccount:ObjectId"], out var id) ? id : Guid.Empty;
    internal string ServiceUpn => config[Prefix + "ServiceAccount:Username"] ?? "";
    internal string BootstrapCache => config[Prefix + "BootstrapAuthorizationCache"] ?? "";
    internal string KeyId => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(EncryptionKey!))[..16].ToLowerInvariant();
    internal string Context => $"CampusFlow.CF028.Delegated.v1|{Environment}|{MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId:D}|{ClientId:D}|{ServiceId:D}|{ServiceUpn.ToLowerInvariant()}";
    internal byte[]? EncryptionKey
    {
        get { try { var key = Convert.FromBase64String(config[Prefix + "TokenEncryptionKey"] ?? ""); return key.Length == 32 ? key : null; } catch (FormatException) { return null; } }
    }
    internal bool AuthorizationConfigured
    {
        get
        {
            try
            {
                var label = config["App:EnvironmentLabel"];
                return (Environment == "nonprod" || string.IsNullOrWhiteSpace(label) || string.Equals(label, "PRODUCTION", StringComparison.OrdinalIgnoreCase)) &&
                    config["ASPNETCORE_ENVIRONMENT"] == (Environment == "nonprod" ? "Staging" : "Production") && config.GetValue<bool>("App:DisablePII") &&
                    string.Equals(config[Prefix + "Backend"], "MicrosoftWriteback", StringComparison.Ordinal) &&
                    Guid.TryParse(config[Prefix + "DirectoryTenantId"], out var tenant) && tenant == MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId &&
                    ClientId != Guid.Empty && ServiceId != Guid.Empty && EncryptionKey is not null &&
                    ServiceUpn == ServiceUpn.Trim() && ServiceUpn.Split('@') is [var local, var domain] && local.Length > 0 &&
                    string.Equals(domain, "nelson.edu", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(config["App:SelfUrl"], UriKind.Absolute, out var host) && host.Scheme == "https" && host.IsDefaultPort &&
                    host.Host == (Environment == "nonprod" ? "my-np.nelson.edu" : "my.nelson.edu") && host.AbsolutePath == "/" &&
                    host.UserInfo == "" && host.Query == "" && host.Fragment == "";
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return false; }
        }
    }
    internal bool RuntimeConfigured
    {
        get
        {
            try { return AuthorizationConfigured && config.GetValue<bool>(Prefix + "Enabled") &&
                config.GetValue<bool>(Prefix + (Environment == "nonprod" ? "EnableNonProductionWrites" : "EnableProductionWrites")) &&
                !config.GetValue<bool>(Prefix + (Environment == "nonprod" ? "EnableProductionWrites" : "EnableNonProductionWrites")) && AllowedUpns.Length > 0; }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return false; }
        }
    }
    internal string[] AllowedUpns
    {
        get
        {
            var values = config.GetSection(Prefix + "AllowedStudentUpns").GetChildren().Select(x => x.Value).ToArray();
            return values.Length == 0 || values.Any(x => !StudentUpn(x)) ? [] : values.Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
    internal bool Allowed(string? upn) => StudentUpn(upn) && AllowedUpns.Contains(upn!, StringComparer.OrdinalIgnoreCase);
    private static bool StudentUpn(string? upn) => upn is not null && upn == upn.Trim() && upn.Length <= 113 && upn.Split('@') is [var local, var domain] &&
        local.Length is > 0 and <= 64 && local.All(x => char.IsAsciiLetterOrDigit(x) || "'.-_!#^~".Contains(x)) && domain.Equals("my.nelson.edu", StringComparison.OrdinalIgnoreCase);
}
