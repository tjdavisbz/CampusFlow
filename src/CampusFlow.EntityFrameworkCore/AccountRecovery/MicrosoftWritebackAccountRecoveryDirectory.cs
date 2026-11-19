using System;
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
using Volo.Abp.Auditing;

namespace CampusFlow.AccountRecovery;

/// <summary>Microsoft's delegated reset operation supports hybrid writeback. A POST is sent once; only status GETs are polled.</summary>
[DisableAuditing]
public sealed class MicrosoftWritebackAccountRecoveryDirectory : IAccountRecoveryDirectory
{
    private const string Graph = "https://graph.microsoft.com/v1.0/";
    private const string PasswordMethod = "28c10230-6103-485e-b985-444c60001490";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly WritebackRecoverySettings _settings;
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<RecoveryAccessToken>> _token;
    private readonly Func<bool> _authorized;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    public MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config) : this(config, new DelegatedRecoveryCredential(config)) { }
    private MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config, DelegatedRecoveryCredential credential)
        : this(config, SharedClient, credential.TokenAsync, () => credential.HasAuthorization, Task.Delay) { }
    internal MicrosoftWritebackAccountRecoveryDirectory(IConfiguration config, HttpClient http, Func<CancellationToken, Task<RecoveryAccessToken>> token,
        Func<bool> authorized, Func<TimeSpan, CancellationToken, Task> delay)
    { _settings = new(config); _http = http; _token = token; _authorized = authorized; _delay = delay; }
    public bool IsConfigured => _settings.RuntimeConfigured && _authorized();
    public bool SupportsSynchronizedAccounts => IsConfigured;
    public async Task<RecoveryDirectoryAccount?> FindAsync(string upn, CancellationToken ct = default)
    {
        if (!IsConfigured || !_settings.Allowed(upn)) return null;
        try { return await ReadAsync(Uri.EscapeDataString(upn), upn, null, await TokenAsync(ct), ct); }
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
        try
        {
            token = await TokenAsync(ct);
            if (await ReadAsync(account.ObjectId.ToString("D"), account.UserPrincipalName, account.ObjectId, token, ct) != account)
                return RecoveryPasswordWriteResult.Unavailable;
        }
        catch (Exception ex) when (Expected(ex)) { return RecoveryPasswordWriteResult.Unavailable; }
        if (!IsConfigured || !_settings.Allowed(account.UserPrincipalName) || !DelegatedRecoveryCredential.TokenMatches(token, _settings))
            return RecoveryPasswordWriteResult.Unavailable;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var write = Request(HttpMethod.Post, Graph + "users/" + account.ObjectId.ToString("D") + "/authentication/methods/" + PasswordMethod + "/resetPassword", token);
        write.Content = new StringContent(JsonSerializer.Serialize(new { newPassword = password }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(write, deadline.Token);
            if (response.StatusCode == HttpStatusCode.BadRequest && MicrosoftAccountRecoveryDirectory.IsPasswordPolicyError(await response.Content.ReadAsStringAsync(deadline.Token)))
                return RecoveryPasswordWriteResult.PolicyRejected;
            if (response.StatusCode != HttpStatusCode.Accepted)
                return response.IsSuccessStatusCode || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout
                    ? RecoveryPasswordWriteResult.Unknown : RecoveryPasswordWriteResult.Unavailable;
            var location = response.Headers.Location;
            if (!ValidOperation(location, account.ObjectId)) return RecoveryPasswordWriteResult.Unknown;
            var delay = RetryDelay(response);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await _delay(delay, deadline.Token);
                using var poll = Request(HttpMethod.Get, location!.AbsoluteUri, token);
                using var statusResponse = await _http.SendAsync(poll, deadline.Token);
                if (statusResponse.StatusCode != HttpStatusCode.OK) return RecoveryPasswordWriteResult.Unknown;
                var body = await statusResponse.Content.ReadAsStringAsync(deadline.Token);
                var state = JsonNode.Parse(body) as JsonObject;
                if (state?["id"] is JsonValue operationId && operationId.GetValue<string>() != location.Segments.Last()) return RecoveryPasswordWriteResult.Unknown;
                switch (state?["status"]?.GetValue<string>())
                {
                    case "succeeded": return RecoveryPasswordWriteResult.Accepted;
                    case "failed": return MicrosoftAccountRecoveryDirectory.IsPasswordPolicyError(body) ? RecoveryPasswordWriteResult.PolicyRejected : RecoveryPasswordWriteResult.Unknown;
                    case "notStarted": case "running": delay = RetryDelay(statusResponse); break;
                    default: return RecoveryPasswordWriteResult.Unknown;
                }
            }
            return RecoveryPasswordWriteResult.Unknown;
        }
        catch (Exception ex) when (Expected(ex)) { return RecoveryPasswordWriteResult.Unknown; }
    }
    internal static bool ValidOperation(Uri? location, Guid student) => location is { IsAbsoluteUri: true } &&
        location.Scheme == "https" && location.Host == "graph.microsoft.com" && location.IsDefaultPort && location.UserInfo == "" &&
        location.Query == "" && location.Fragment == "" && Regex.IsMatch(location.AbsolutePath,
            "^/v1\\.0/users/" + student.ToString("D") + "/authentication/operations/[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant);
    private static TimeSpan RetryDelay(HttpResponseMessage response) => TimeSpan.FromSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 2, 1, 5));
    private static HttpRequestMessage Request(HttpMethod method, string url, RecoveryAccessToken token)
    { var message = new HttpRequestMessage(method, url); message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value); return message; }
    private static bool Expected(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or
        FormatException or ArgumentException or System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or Microsoft.Identity.Client.MsalException;
}
