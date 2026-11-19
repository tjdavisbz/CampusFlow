using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using Volo.Abp.Auditing;

namespace CampusFlow.AccountRecovery;

/// <summary>Operator authorization honors MFA. Student requests only refresh the encrypted authorized session; no password grant or MFA bypass.</summary>
[DisableAuditing]
public sealed class DelegatedRecoveryCredential
{
    private readonly WritebackRecoverySettings _settings;
    private readonly RecoveryTokenCache _cache;
    public DelegatedRecoveryCredential(IConfiguration config) : this(config, null) { }
    private DelegatedRecoveryCredential(IConfiguration config, string? root) { _settings = new(config); _cache = new(_settings, root); }
    public static DelegatedRecoveryCredential ForOperator(IConfiguration config, string cacheRoot)
    {
        if (!System.IO.Path.IsPathFullyQualified(cacheRoot)) throw new ArgumentException("Use a private absolute cache directory.");
        return new(config, cacheRoot);
    }
    public bool AuthorizationConfigured => _settings.AuthorizationConfigured;
    public bool HasAuthorization => AuthorizationConfigured && _cache.Exists;
    private IPublicClientApplication Client() => PublicClientApplicationBuilder.Create(_settings.ClientId.ToString("D"))
        .WithAuthority("https://login.microsoftonline.com/" + MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId.ToString("D"))
        .WithRedirectUri("http://localhost").Build();
    public async Task AuthorizeAsync(Func<DeviceCodeResult, Task> showDeviceCode, CancellationToken ct = default)
    {
        if (!AuthorizationConfigured) throw new InvalidOperationException("Recovery authorization setup is incomplete.");
        using var lease = await _cache.LockAsync(ct);
        var client = Client();
        byte[]? serialized = null;
        client.UserTokenCache.SetAfterAccess(args => { if (args.HasStateChanged) serialized = args.TokenCache.SerializeMsalV3(); });
        var result = await client.AcquireTokenWithDeviceCode(WritebackRecoverySettings.Scopes, showDeviceCode).ExecuteAsync(ct);
        if (!AccountMatches(result.Account) || !TokenMatches(FromResult(result), _settings) || serialized is null)
            throw new InvalidOperationException("Sign in with the configured recovery service account.");
        await _cache.WriteAsync(serialized, ct);
    }
    public async Task VerifyAuthorizationAsync(CancellationToken ct = default) => _ = await AcquireTokenAsync(ct, true);
    internal Task<RecoveryAccessToken> TokenAsync(CancellationToken ct) => AcquireTokenAsync(ct, false);
    private async Task<RecoveryAccessToken> AcquireTokenAsync(CancellationToken ct, bool forceRefresh)
    {
        if (!HasAuthorization) throw new InvalidOperationException("Recovery authorization is unavailable.");
        using var lease = await _cache.LockAsync(ct);
        var client = Client();
        var serialized = await _cache.ReadAsync(ct);
        client.UserTokenCache.SetBeforeAccess(args => args.TokenCache.DeserializeMsalV3(serialized));
        client.UserTokenCache.SetAfterAccess(args => { if (args.HasStateChanged) serialized = args.TokenCache.SerializeMsalV3(); });
        var accounts = (await client.GetAccountsAsync()).Where(AccountMatches).ToArray();
        if (accounts.Length != 1) throw new InvalidOperationException("Recovery authorization is unavailable.");
        var result = await client.AcquireTokenSilent(WritebackRecoverySettings.Scopes, accounts[0]).WithForceRefresh(forceRefresh).ExecuteAsync(ct);
        if (!AccountMatches(result.Account) || !TokenMatches(FromResult(result), _settings))
            throw new InvalidOperationException("Recovery authorization is unavailable.");
        await _cache.WriteAsync(serialized, ct);
        return FromResult(result);
    }
    private bool AccountMatches(IAccount? account) => account is not null &&
        string.Equals(account.Username, _settings.ServiceUpn, StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParse(account.HomeAccountId.ObjectId, out var id) && id == _settings.ServiceId &&
        Guid.TryParse(account.HomeAccountId.TenantId, out var tenant) && tenant == MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId;
    // MSAL validates the authentication response. Graph access tokens are opaque;
    // bind the trusted result metadata without depending on the token's format.
    private RecoveryAccessToken FromResult(AuthenticationResult result) => new(result.AccessToken,
        Guid.TryParse(result.TenantId, out var tenant) ? tenant : Guid.Empty,
        Guid.TryParse(result.Account?.HomeAccountId.ObjectId, out var id) ? id : Guid.Empty,
        result.Account?.Username ?? "", _settings.ClientId, "https://graph.microsoft.com", result.Scopes.ToArray(), result.ExpiresOn);
    internal static bool TokenMatches(RecoveryAccessToken token, WritebackRecoverySettings settings) =>
        !string.IsNullOrWhiteSpace(token.Value) && token.TenantId == MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId &&
        token.ServiceId == settings.ServiceId && token.ClientId == settings.ClientId &&
        string.Equals(token.ServiceUpn, settings.ServiceUpn, StringComparison.OrdinalIgnoreCase) &&
        token.Resource == "https://graph.microsoft.com" && token.ExpiresOn > DateTimeOffset.UtcNow.AddSeconds(60) &&
        WritebackRecoverySettings.Scopes.All(required => token.Scopes.Any(scope =>
            string.Equals(scope, required, StringComparison.Ordinal) ||
            string.Equals(scope, required["https://graph.microsoft.com/".Length..], StringComparison.Ordinal)));
}

internal sealed record RecoveryAccessToken(string Value, Guid TenantId, Guid ServiceId, string ServiceUpn,
    Guid ClientId, string Resource, string[] Scopes, DateTimeOffset ExpiresOn);
