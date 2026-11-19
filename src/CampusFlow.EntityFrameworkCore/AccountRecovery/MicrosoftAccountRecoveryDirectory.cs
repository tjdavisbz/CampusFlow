using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.AccountRecovery;

/// <summary>
/// A narrowly scoped cloud-account reset adapter. A caller must atomically claim a
/// one-use recovery session before ResetAsync; this adapter never retries a PATCH.
/// Synchronized accounts are deliberately unavailable until a supported hybrid
/// reset path has been confirmed, rather than assuming a cloud PATCH writes to AD.
/// </summary>
[DisableAuditing]
public sealed class MicrosoftAccountRecoveryDirectory : IAccountRecoveryDirectory, ITransientDependency
{
    public bool SupportsSynchronizedAccounts => false;
    internal static readonly Guid NelsonDirectoryTenantId = new("e0ffb7a4-8410-4443-a99e-a8276ad387b1");
    private const string Prefix = "PasswordRecovery:";
    private const string GraphRoot = "https://graph.microsoft.com/v1.0/";
    private const string PasswordPermission = "User-PasswordProfile.ReadWrite.All";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(45) };
    private readonly IConfiguration _configuration;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string>> _token;
    private readonly object _credentialGate = new();
    private ManagedIdentityCredential? _credential;
    private string? _credentialClientId;

    public MicrosoftAccountRecoveryDirectory(IConfiguration configuration)
        : this(configuration, SharedClient, null) { }

    internal MicrosoftAccountRecoveryDirectory(IConfiguration configuration, HttpClient client,
        Func<CancellationToken, Task<string>>? token)
    {
        _configuration = configuration;
        _client = client;
        _token = token ?? ManagedIdentityTokenAsync;
    }

    public bool IsConfigured
    {
        get
        {
            try
            {
                var nonprod = string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase);
                var production = string.IsNullOrWhiteSpace(_configuration["App:EnvironmentLabel"]) ||
                    string.Equals(_configuration["App:EnvironmentLabel"], "PRODUCTION", StringComparison.OrdinalIgnoreCase);
                return _configuration.GetValue<bool>(Prefix + "Enabled") &&
                    _configuration.GetValue<bool>("App:DisablePII") &&
                    _configuration["ASPNETCORE_ENVIRONMENT"] == "Production" &&
                    (nonprod || production) &&
                    _configuration.GetValue<bool>(Prefix + (nonprod ? "EnableNonProductionWrites" : "EnableProductionWrites")) &&
                    !_configuration.GetValue<bool>(Prefix + (nonprod ? "EnableProductionWrites" : "EnableNonProductionWrites")) &&
                    Host(_configuration["App:SelfUrl"], nonprod ? "my-np.nelson.edu" : "my.nelson.edu") &&
                    Guid.TryParse(_configuration[Prefix + "DirectoryTenantId"], out var directory) && directory == NelsonDirectoryTenantId &&
                    Guid.TryParse(_configuration[Prefix + "ManagedIdentityClientId"], out var identity) && identity != Guid.Empty &&
                    AllowedUpns().Length > 0;
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
            {
                return false;
            }
        }
    }

    public async Task<RecoveryDirectoryAccount?> FindAsync(string userPrincipalName, CancellationToken ct = default)
    {
        if (!IsConfigured || !IsAllowed(userPrincipalName)) return null;
        try
        {
            var token = await TokenAsync(ct);
            return await ReadAccountAsync(Uri.EscapeDataString(userPrincipalName.Trim()), userPrincipalName, null, token, ct);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            // Deliberately do not log raw SDK/HTTP exceptions, tokens or Graph bodies.
            return null;
        }
    }

    public async Task<RecoveryPasswordWriteResult> ResetAsync(RecoveryDirectoryAccount account, string password,
        CancellationToken ct = default)
    {
        if (!IsConfigured || account.ObjectId == Guid.Empty || !IsAllowed(account.UserPrincipalName) ||
            !account.Enabled || !account.IsMember || account.IsPrivileged || account.IsSynchronized)
            return RecoveryPasswordWriteResult.Unavailable;
        if (string.IsNullOrEmpty(password) || password.Length > 256) return RecoveryPasswordWriteResult.PolicyRejected;

        string token;
        try
        {
            token = await TokenAsync(ct);
            var current = await ReadAccountAsync(account.ObjectId.ToString("D"), account.UserPrincipalName, account.ObjectId, token, ct);
            if (current is null || current != account) return RecoveryPasswordWriteResult.Unavailable;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return RecoveryPasswordWriteResult.Unavailable; // Nothing was submitted.
        }

        // Re-check an operator's environment/allowlist switch after network preflight.
        if (!IsConfigured || !IsAllowed(account.UserPrincipalName) || !TokenMatchesIdentity(token))
            return RecoveryPasswordWriteResult.Unavailable;
        using var request = Request(HttpMethod.Patch, "users/" + account.ObjectId.ToString("D"), token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            passwordProfile = new { password, forceChangePasswordNextSignIn = false }
        }), Encoding.UTF8, "application/json");
        try
        {
            // No retry handler or retry loop: the workflow owns durable single-use state.
            using var response = await _client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NoContent) return RecoveryPasswordWriteResult.Accepted;
            if (response.IsSuccessStatusCode || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                return RecoveryPasswordWriteResult.Unknown;
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (IsPasswordPolicyError(body)) return RecoveryPasswordWriteResult.PolicyRejected;
            }
            return RecoveryPasswordWriteResult.Unavailable;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            // A timeout/cancellation may happen after Graph changed the password.
            return RecoveryPasswordWriteResult.Unknown;
        }
    }

    private async Task<RecoveryDirectoryAccount?> ReadAccountAsync(string identifier, string expectedUpn,
        Guid? expectedId, string token, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "users/" + identifier +
            "?$select=id,userPrincipalName,accountEnabled,userType,onPremisesSyncEnabled", token);
        using var response = await _client.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        var user = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
        if (user is null || !Guid.TryParse(user["id"]?.GetValue<string>(), out var id) || id == Guid.Empty ||
            expectedId.HasValue && id != expectedId.Value ||
            user["userPrincipalName"]?.GetValue<string>() is not string upn || !IsAllowed(upn) ||
            !string.Equals(upn, expectedUpn.Trim(), StringComparison.OrdinalIgnoreCase) ||
            user["accountEnabled"]?.GetValue<bool>() != true ||
            !string.Equals(user["userType"]?.GetValue<string>(), "Member", StringComparison.Ordinal) ||
            !user.ContainsKey("onPremisesSyncEnabled")) return null;

        // Graph returns null for an account that has never been synchronized.
        if (user["onPremisesSyncEnabled"] is not null && user["onPremisesSyncEnabled"]!.GetValue<bool>()) return null;
        using var roleRequest = Request(HttpMethod.Get, "users/" + id.ToString("D") +
            "/transitiveMemberOf", token);
        using var roleResponse = await _client.SendAsync(roleRequest, ct);
        if (roleResponse.StatusCode != HttpStatusCode.OK) return null;
        var membership = JsonNode.Parse(await roleResponse.Content.ReadAsStringAsync(ct)) as JsonObject;
        // Avoid advanced OData-cast queries: those use an eventually consistent index.
        // The standard relationship supplies type + ID even with least-privileged reads.
        // Unread pages or unknown types cannot establish an unprivileged account.
        if (membership?["value"] is not JsonArray memberships || membership["@odata.nextLink"] is not null) return null;
        foreach (var member in memberships)
        {
            if (member is not JsonObject entry || entry["@odata.type"]?.GetValue<string>() is not string type ||
                !Guid.TryParse(entry["id"]?.GetValue<string>(), out var memberId) || memberId == Guid.Empty ||
                type is not ("#microsoft.graph.group" or "#microsoft.graph.administrativeUnit")) return null;
        }
        return new(id, upn, true, true, false, false);
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Account recovery directory is unavailable.");
        var token = await _token(ct);
        // The trusted managed-identity credential obtains this token. Checking its
        // audience-independent tenant/client claims is an extra fail-closed pin;
        // Graph remains responsible for signature/audience/permission validation.
        if (!TokenMatchesIdentity(token)) throw new InvalidOperationException("Account recovery identity could not be verified.");
        return token;
    }

    private async Task<string> ManagedIdentityTokenAsync(CancellationToken ct)
    {
        var clientId = _configuration[Prefix + "ManagedIdentityClientId"]!;
        ManagedIdentityCredential credential;
        lock (_credentialGate)
        {
            if (_credential is null || _credentialClientId != clientId)
            {
                _credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
                _credentialClientId = clientId;
            }
            credential = _credential;
        }
        return (await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), ct)).Token;
    }

    private bool TokenMatchesIdentity(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[1].Length > 32768) return false;
            var value = parts[1].Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
            using var payload = JsonDocument.Parse(Convert.FromBase64String(value));
            var root = payload.RootElement;
            var client = root.TryGetProperty("appid", out var appId) ? appId.GetString() :
                root.TryGetProperty("azp", out var azp) ? azp.GetString() : null;
            return root.TryGetProperty("tid", out var tid) && Guid.TryParse(tid.GetString(), out var directory) &&
                directory == NelsonDirectoryTenantId && Guid.TryParse(client, out var identity) &&
                Guid.TryParse(_configuration[Prefix + "ManagedIdentityClientId"], out var expected) && identity == expected &&
                root.TryGetProperty("roles", out var permissions) && permissions.ValueKind == JsonValueKind.Array &&
                permissions.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == PasswordPermission);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private string[] AllowedUpns()
    {
        var values = _configuration.GetSection(Prefix + "AllowedStudentUpns").GetChildren()
            .Select(x => x.Value).ToArray();
        if (values.Length == 0 || values.Any(x => !StudentUpn(x))) return [];
        return values.Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private bool IsAllowed(string? upn) => StudentUpn(upn) && AllowedUpns().Contains(upn!.Trim(), StringComparer.OrdinalIgnoreCase);
    private static bool StudentUpn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 113) return false;
        var parts = value.Split('@');
        return parts.Length == 2 && parts[0].Length is > 0 and <= 64 &&
            parts[0].All(x => char.IsAsciiLetterOrDigit(x) || "'.-_!#^~".Contains(x)) &&
            string.Equals(parts[1], "my.nelson.edu", StringComparison.OrdinalIgnoreCase);
    }
    private static bool Host(string? value, string expected) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == expected && uri.IsDefaultPort && uri.AbsolutePath == "/" &&
        uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "";
    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, GraphRoot + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
    private static bool IsExpectedFailure(Exception exception) => exception is HttpRequestException or OperationCanceledException or
        JsonException or InvalidOperationException or FormatException or ArgumentException or Azure.Identity.AuthenticationFailedException or
        Azure.Identity.CredentialUnavailableException;
    internal static bool IsPasswordPolicyError(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            var code = error?["code"]?.GetValue<string>() ?? "";
            if (new[] { "PasswordTooShort", "PasswordTooWeak", "PasswordDoesNotComplyComplexity", "PasswordDoesNotMeetRequirements",
                    "PasswordBanned", "PasswordInHistory", "PasswordSameAsOldPassword", "InvalidPassword", "PasswordPolicyError" }
                .Contains(code, StringComparer.OrdinalIgnoreCase)) return true;
            var message = error?["message"]?.GetValue<string>() ?? "";
            return code == "Request_BadRequest" && message.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                new[] { "complexity", "policy", "banned", "weak", "history", "reuse", "length", "too short" }
                    .Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
