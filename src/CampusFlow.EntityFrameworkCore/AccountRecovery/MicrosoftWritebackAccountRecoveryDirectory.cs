using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Auditing;

namespace CampusFlow.AccountRecovery;

/// <summary>Microsoft's delegated reset operation supports hybrid writeback. A POST is sent once; only status GETs are polled.</summary>
[DisableAuditing]
public sealed class MicrosoftWritebackAccountRecoveryDirectory : IAccountRecoveryDirectory
{
    private const string Graph = "https://graph.microsoft.com/v1.0/";
    private const string PasswordMethod = "28c10230-6103-485e-b985-444c60001490";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly WritebackRecoverySettings _settings;
    private readonly Action<string, int?>? _diagnostic;
    private readonly TimeSpan _lookupTimeout;
    private readonly TimeSpan _operationTimeout;
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<RecoveryAccessToken>> _token;
    private readonly Func<bool> _authorized;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    public MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config, ILogger<MicrosoftWritebackAccountRecoveryDirectory>? logger = null) : this(config, new DelegatedRecoveryCredential(config), logger) { }
    private MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config, DelegatedRecoveryCredential credential, ILogger<MicrosoftWritebackAccountRecoveryDirectory>? logger)
        : this(config, SharedClient, credential.TokenAsync, () => credential.HasAuthorization, Task.Delay,
            (stage, status) => logger?.LogWarning("Account recovery provider confirmation diagnostic: Stage={Stage}, HttpStatus={HttpStatus}", stage, status)) { }
    internal MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config, HttpClient http, Func<CancellationToken, Task<RecoveryAccessToken>> token,
        Func<bool> authorized, Func<TimeSpan, CancellationToken, Task> delay, Action<string, int?>? diagnostic = null,
        TimeSpan? lookupTimeout = null, TimeSpan? operationTimeout = null)
    { _settings = new(config); _http = http; _token = token; _authorized = authorized; _delay = delay; _diagnostic = diagnostic;
        _lookupTimeout = lookupTimeout ?? TimeSpan.FromSeconds(20);
        _operationTimeout = operationTimeout ?? TimeSpan.FromMinutes(2);
    }
    public bool IsConfigured => _settings.RuntimeConfigured && _authorized();
    public bool SupportsSynchronizedAccounts => IsConfigured;
    public async Task<RecoveryDirectoryAccount?> FindAsync(string upn, CancellationToken ct = default)
    {
        if (!IsConfigured || !_settings.Allowed(upn)) return null;
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(ct); lookup.CancelAfter(_lookupTimeout);
        try { return await ReadAsync(Uri.EscapeDataString(upn), upn, null, await TokenAsync(lookup.Token), lookup.Token); }
        catch (Exception ex) when (Expected(ex)) { return null; }
    }
    private async Task<RecoveryAccessToken> TokenAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Recovery is unavailable.");
        var token = await _token(ct);
        if (!DelegatedRecoveryCredential.TokenMatches(token, _settings)) throw new InvalidOperationException("Recovery authorization is unavailable.");
        return token;
    }
    private async Task<RecoveryDirectoryAccount?> ReadAsync(string identifier, string expectedUpn, Guid? expectedId, RecoveryAccessToken token, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, Graph + "users/" + identifier + "?$select=id,userPrincipalName,accountEnabled,userType,onPremisesSyncEnabled", token);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        var user = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
        if (user is null || !Guid.TryParse(user["id"]?.GetValue<string>(), out var id) || id == Guid.Empty || id == _settings.ServiceId ||
            expectedId.HasValue && expectedId.Value != id || user["userPrincipalName"]?.GetValue<string>() is not string upn || !_settings.Allowed(upn) ||
            !string.Equals(expectedUpn, upn, StringComparison.OrdinalIgnoreCase) || user["accountEnabled"]?.GetValue<bool>() != true ||
            user["userType"]?.GetValue<string>() != "Member" || !user.ContainsKey("onPremisesSyncEnabled")) return null;
        using var roles = Request(HttpMethod.Get, Graph + "users/" + id.ToString("D") + "/transitiveMemberOf", token);
        using var roleResponse = await _http.SendAsync(roles, ct);
        if (roleResponse.StatusCode != HttpStatusCode.OK) return null;
        var membership = JsonNode.Parse(await roleResponse.Content.ReadAsStringAsync(ct)) as JsonObject;
        if (membership?["value"] is not JsonArray values || membership["@odata.nextLink"] is not null) return null;
        foreach (var member in values)
            if (member is not JsonObject entry || entry["@odata.type"]?.GetValue<string>() is not string kind ||
                !Guid.TryParse(entry["id"]?.GetValue<string>(), out var memberId) || memberId == Guid.Empty ||
                kind is not ("#microsoft.graph.group" or "#microsoft.graph.administrativeUnit")) return null;
        return new(id, upn, true, true, false, user["onPremisesSyncEnabled"]?.GetValue<bool>() ?? false);
    }
    public async Task<RecoveryPasswordWriteResult> ResetAsync(RecoveryDirectoryAccount account, string password, CancellationToken ct = default)
    {
        if (!IsConfigured || !_settings.Allowed(account.UserPrincipalName) || account.ObjectId == Guid.Empty || account.ObjectId == _settings.ServiceId ||
            !account.Enabled || !account.IsMember || account.IsPrivileged) return RecoveryPasswordWriteResult.Unavailable;
        if (string.IsNullOrEmpty(password) || password.Length > 256) return RecoveryPasswordWriteResult.PolicyRejected;
        RecoveryAccessToken token;
        using var preflight = CancellationTokenSource.CreateLinkedTokenSource(ct); preflight.CancelAfter(_lookupTimeout);
        try
        {
            token = await TokenAsync(preflight.Token);
            if (await ReadAsync(account.ObjectId.ToString("D"), account.UserPrincipalName, account.ObjectId, token, preflight.Token) != account)
                return RecoveryPasswordWriteResult.Unavailable;
        }
        catch (Exception ex) when (Expected(ex)) { return RecoveryPasswordWriteResult.Unavailable; }
        if (!IsConfigured || !_settings.Allowed(account.UserPrincipalName) || !DelegatedRecoveryCredential.TokenMatches(token, _settings))
            return RecoveryPasswordWriteResult.Unavailable;
        if (ct.IsCancellationRequested) return RecoveryPasswordWriteResult.Unavailable;
        // Once a reset can be submitted, finish its status check independently
        // of a browser disconnect, without retrying the POST.
        using var deadline = new CancellationTokenSource(_operationTimeout);
        using var write = Request(HttpMethod.Post, Graph + "users/" + account.ObjectId.ToString("D") + "/authentication/methods/" + PasswordMethod + "/resetPassword", token);
        write.Content = new StringContent(JsonSerializer.Serialize(new { newPassword = password }), Encoding.UTF8, "application/json");
        var phase = "reset-response";
        try
        {
            using var response = await _http.SendAsync(write, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            phase = "reset-body";
            if (response.StatusCode == HttpStatusCode.BadRequest && MicrosoftAccountRecoveryDirectory.IsPasswordPolicyError(await response.Content.ReadAsStringAsync(deadline.Token)))
                return RecoveryPasswordWriteResult.PolicyRejected;
            if (response.StatusCode != HttpStatusCode.Accepted)
                return response.IsSuccessStatusCode || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout
                    ? RecoveryPasswordWriteResult.Unknown : RecoveryPasswordWriteResult.Unavailable;
            var location = response.Headers.Location;
            if (!ValidOperation(location, account.ObjectId)) return Unconfirmed("operation-location");
            var delay = RetryDelay(response);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                phase = "confirmation-wait";
                await _delay(delay, deadline.Token);
                using var poll = Request(HttpMethod.Get, location!.AbsoluteUri, token);
                phase = "confirmation-response";
                using var statusResponse = await _http.SendAsync(poll, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (statusResponse.StatusCode != HttpStatusCode.OK) return Unconfirmed("confirmation-http", (int)statusResponse.StatusCode);
                phase = "confirmation-body";
                var body = await statusResponse.Content.ReadAsStringAsync(deadline.Token);
                var state = JsonNode.Parse(body) as JsonObject;
                if (state?["id"] is JsonValue operationId && operationId.GetValue<string>() != location.Segments.Last()) return Unconfirmed("operation-id");
                switch (state?["status"]?.GetValue<string>())
                {
                    case "succeeded": return RecoveryPasswordWriteResult.Accepted;
                    case "failed": return IsOperationPolicyRejection(state!, body) ? RecoveryPasswordWriteResult.PolicyRejected : Unconfirmed("operation-failed");
                    case "notStarted": case "running": delay = RetryDelay(statusResponse); break;
                    default: return Unconfirmed("operation-status");
                }
            }
            return Unconfirmed("confirmation-timeout");
        }
        catch (Exception ex) when (Expected(ex))
        {
            var kind = ex is OperationCanceledException ? "timeout" : ex is JsonException or InvalidOperationException or FormatException or ArgumentException ? "format" : "transport";
            return Unconfirmed(phase + "-" + kind);
        }
    }
    private RecoveryPasswordWriteResult Unconfirmed(string stage, int? status = null)
    { _diagnostic?.Invoke(stage, status); return RecoveryPasswordWriteResult.Unknown; }
    private static bool IsOperationPolicyRejection(JsonObject state, string body)
    {
        if (MicrosoftAccountRecoveryDirectory.IsPasswordPolicyError(body)) return true;
        // Password-reset operations use statusDetail, rather than the normal
        // Graph error envelope. Only recognize a specific policy failure code.
        return state["statusDetail"] is JsonValue detail && detail.TryGetValue<string>(out var text) &&
            (MicrosoftAccountRecoveryDirectory.IsPasswordPolicyError(text) ||
             Regex.IsMatch(text, "(?<![A-Za-z0-9_])PasswordPolicyError(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
    internal static bool ValidOperation(Uri? location, Guid student) => location is { IsAbsoluteUri: true } &&
        location.Scheme == "https" && location.Host == "graph.microsoft.com" && location.IsDefaultPort && location.UserInfo == "" &&
        ValidRoutingQuery(location.Query) && location.Fragment == "" && Regex.IsMatch(location.AbsolutePath,
            "^/v1\\.0/users/" + student.ToString("D") + "/authentication/operations/[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant);
    // The reset API's documented Location includes these datacenter routing
    // parameters. Preserve them when polling, while rejecting all other query
    // keys, duplicates, control characters and redirect-like values.
    private static bool ValidRoutingQuery(string query)
    {
        if (query.Length == 0) return true;
        if (query.Length > 256 || query[0] != '?') return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in query[1..].Split('&'))
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2 || pair[0] is not ("aadgdc" or "aadgsu") || !seen.Add(pair[0])) return false;
            string value;
            try { value = Uri.UnescapeDataString(pair[1]); }
            catch (UriFormatException) { return false; }
            if (!Regex.IsMatch(value, "^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)) return false;
        }
        return true;
    }
    private static TimeSpan RetryDelay(HttpResponseMessage response) => TimeSpan.FromSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 2, 1, 5));
    private static HttpRequestMessage Request(HttpMethod method, string url, RecoveryAccessToken token)
    { var message = new HttpRequestMessage(method, url); message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value); return message; }
    private static bool Expected(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or
        FormatException or ArgumentException or System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or Microsoft.Identity.Client.MsalException;
}
