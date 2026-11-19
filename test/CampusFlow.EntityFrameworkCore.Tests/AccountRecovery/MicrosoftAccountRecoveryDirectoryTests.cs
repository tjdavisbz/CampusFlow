using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.AccountRecovery;

public sealed class MicrosoftAccountRecoveryDirectoryTests
{
    private const string Upn = "recovery.test@my.nelson.edu";
    private static readonly Guid ObjectId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid IdentityClientId = new("11111111-2222-3333-4444-555555555555");
    private static RecoveryDirectoryAccount Account() => new(ObjectId, Upn, true, true, false, false);
    private static Dictionary<string, string?> Settings() => new()
    {
        ["PasswordRecovery:Enabled"] = "true", ["PasswordRecovery:EnableNonProductionWrites"] = "true",
        ["PasswordRecovery:EnableProductionWrites"] = "false", ["App:DisablePII"] = "true",
        ["App:EnvironmentLabel"] = "NON-PRODUCTION", ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["App:SelfUrl"] = "https://my-np.nelson.edu", ["PasswordRecovery:DirectoryTenantId"] = MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId.ToString(),
        ["PasswordRecovery:ManagedIdentityClientId"] = IdentityClientId.ToString(), ["PasswordRecovery:AllowedStudentUpns:0"] = Upn
    };
    private static (MicrosoftAccountRecoveryDirectory Adapter, Handler Http, TokenSource Token) Setup(Dictionary<string, string?>? settings = null)
    {
        var http = new Handler(); var token = new TokenSource();
        return (new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? Settings()).Build(), new HttpClient(http), token.Get), http, token);
    }
    [Fact]
    public async Task A_verified_cloud_account_changes_only_its_password_profile_once()
    {
        var (adapter, http, _) = Setup(); Assert.True(adapter.IsConfigured);
        Assert.Equal(Account(), await adapter.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Accepted, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!"));
        var sent = Assert.Single(http.Writes); Assert.Equal("graph.microsoft.com", sent.Host);
        Assert.Equal("/v1.0/users/" + ObjectId.ToString("D"), sent.Path);
        Assert.Equal("passwordProfile", Assert.Single(sent.Body).Key);
        var profile = Assert.IsType<JsonObject>(sent.Body["passwordProfile"]);
        Assert.Equal(2, profile.Count); Assert.Equal("Synthetic-Test-Value-493!", profile["password"]!.GetValue<string>());
        Assert.False(profile["forceChangePasswordNextSignIn"]!.GetValue<bool>());
        Assert.Equal(2, http.UserReads); Assert.Equal(2, http.RoleReads);
    }
    [Theory]
    [InlineData("PasswordRecovery:Enabled", "false")]
    [InlineData("PasswordRecovery:EnableNonProductionWrites", "false")]
    [InlineData("PasswordRecovery:EnableProductionWrites", "true")]
    [InlineData("App:DisablePII", "false")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Development")]
    [InlineData("App:EnvironmentLabel", "unknown")]
    [InlineData("App:SelfUrl", "https://my.nelson.edu")]
    [InlineData("App:SelfUrl", "http://my-np.nelson.edu")]
    [InlineData("App:SelfUrl", "https://my-np.nelson.edu/?unexpected=true")]
    [InlineData("PasswordRecovery:DirectoryTenantId", "11111111-1111-1111-1111-111111111111")]
    [InlineData("PasswordRecovery:ManagedIdentityClientId", "not-a-client-id")]
    [InlineData("PasswordRecovery:ManagedIdentityClientId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("PasswordRecovery:AllowedStudentUpns:0", "")]
    [InlineData("PasswordRecovery:AllowedStudentUpns:1", "outside@another.edu")]
    public async Task Missing_or_cross_environment_guards_make_no_token_or_graph_request(string key, string value)
    {
        var settings = Settings(); settings[key] = value; var (adapter, http, token) = Setup(settings);
        Assert.False(adapter.IsConfigured); Assert.Null(await adapter.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!"));
        Assert.Equal(0, token.Calls); Assert.Equal(0, http.Calls);
    }
    [Fact]
    public async Task Production_needs_only_its_matching_flag_and_url()
    {
        var settings = Settings(); settings["App:EnvironmentLabel"] = "PRODUCTION"; settings["App:SelfUrl"] = "https://my.nelson.edu";
        settings["PasswordRecovery:EnableNonProductionWrites"] = "false"; settings["PasswordRecovery:EnableProductionWrites"] = "true";
        var (adapter, _, _) = Setup(settings); Assert.True(adapter.IsConfigured); Assert.Equal(Account(), await adapter.FindAsync(Upn));
    }
    [Theory]
    [InlineData("outside@my.nelson.edu")]
    [InlineData("recovery.test@nelson.edu")]
    [InlineData("recovery.test@my.nelson.edu.attacker.invalid")]
    [InlineData("../users/recovery.test@my.nelson.edu")]
    [InlineData("recovery.test@my.nelson.edu ")]
    public async Task An_arbitrary_or_malformed_upn_never_reaches_the_directory(string target)
    {
        var (adapter, http, token) = Setup(); Assert.Null(await adapter.FindAsync(target));
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account() with { UserPrincipalName = target }, "Synthetic-Test-Value-493!"));
        Assert.Equal(0, token.Calls); Assert.Equal(0, http.Calls);
    }
    [Theory]
    [InlineData("tid")]
    [InlineData("appid")]
    [InlineData("roles")]
    public async Task Wrong_tenant_identity_or_app_permission_in_a_trusted_credential_result_is_rejected(string changed)
    {
        var (adapter, http, token) = Setup(); token.Changed = changed;
        Assert.Null(await adapter.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!"));
        Assert.Equal(0, http.Calls);
    }
    [Theory]
    [InlineData("accountEnabled", false)]
    [InlineData("userType", "Guest")]
    [InlineData("userPrincipalName", "different@my.nelson.edu")]
    [InlineData("id", "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb")]
    [InlineData("onPremisesSyncEnabled", true)]
    public async Task Live_status_identity_and_sync_are_rechecked_before_the_write(string field, object value)
    {
        var (adapter, http, _) = Setup(); Assert.Equal(Account(), await adapter.FindAsync(Upn));
        http.User[field] = JsonSerializer.SerializeToNode(value);
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Empty(http.Writes);
    }
    [Fact]
    public async Task Synchronized_accounts_have_no_configuration_bypass()
    {
        var settings = Settings(); settings["PasswordRecovery:AllowSynchronizedAccounts"] = "true";
        var (adapter, http, _) = Setup(settings); http.User["onPremisesSyncEnabled"] = true;
        Assert.Null(await adapter.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account() with { IsSynchronized = true }, "Synthetic-Test-Value-493!")); Assert.Empty(http.Writes);
    }
    [Fact]
    public async Task A_missing_sync_property_is_unknown_but_an_explicit_null_means_never_synchronized()
    {
        var (adapter, http, _) = Setup(); http.User.Remove("onPremisesSyncEnabled"); Assert.Null(await adapter.FindAsync(Upn));
        http.User["onPremisesSyncEnabled"] = null; Assert.Equal(Account(), await adapter.FindAsync(Upn));
    }
    [Theory]
    [InlineData("role")]
    [InlineData("forbidden")]
    [InlineData("missing")]
    [InlineData("next")]
    public async Task Privileged_or_unconfirmed_directory_membership_is_never_reset(string scenario)
    {
        var (adapter, http, _) = Setup();
        if (scenario == "role") http.Roles = new JsonObject { ["value"] = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.directoryRole", ["id"] = Guid.NewGuid().ToString() }) };
        if (scenario == "forbidden") http.RoleStatus = HttpStatusCode.Forbidden;
        if (scenario == "missing") http.Roles = new JsonObject();
        if (scenario == "next") http.Roles["@odata.nextLink"] = "https://untrusted.invalid/next";
        Assert.Null(await adapter.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Empty(http.Writes);
    }
    [Fact]
    public async Task Ordinary_group_and_administrative_unit_memberships_are_not_administrator_roles()
    {
        var (adapter, http, _) = Setup(); http.Roles = new JsonObject { ["value"] = new JsonArray(
            new JsonObject { ["@odata.type"] = "#microsoft.graph.group", ["id"] = Guid.NewGuid().ToString() },
            new JsonObject { ["@odata.type"] = "#microsoft.graph.administrativeUnit", ["id"] = Guid.NewGuid().ToString() }) };
        Assert.Equal(Account(), await adapter.FindAsync(Upn));
    }
    [Theory]
    [InlineData(400, "Request_BadRequest", "The password does not comply with complexity requirements.", RecoveryPasswordWriteResult.PolicyRejected)]
    [InlineData(400, "PasswordTooWeak", "Suppressed private error text", RecoveryPasswordWriteResult.PolicyRejected)]
    [InlineData(400, "Request_BadRequest", "A required account property is invalid.", RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(401, "InvalidAuthenticationToken", "Suppressed", RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(403, "Authorization_RequestDenied", "Suppressed", RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(429, "TooManyRequests", "Suppressed", RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(500, "InternalServerError", "Suppressed", RecoveryPasswordWriteResult.Unknown)]
    [InlineData(408, "RequestTimeout", "Suppressed", RecoveryPasswordWriteResult.Unknown)]
    [InlineData(200, "", "", RecoveryPasswordWriteResult.Unknown)]
    [InlineData(202, "", "", RecoveryPasswordWriteResult.Unknown)]
    public async Task Entra_policy_is_authoritative_and_only_204_confirms_success(int status, string code, string message, RecoveryPasswordWriteResult expected)
    {
        var (adapter, http, _) = Setup(); http.WriteStatus = (HttpStatusCode)status;
        http.WriteBody = new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
        Assert.Equal(expected, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Single(http.Writes);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_lost_or_cancelled_mutation_reply_is_unknown_and_is_not_retried(bool cancelled)
    {
        var (adapter, http, _) = Setup(); http.LoseWrite = !cancelled; http.CancelWrite = cancelled;
        Assert.Equal(RecoveryPasswordWriteResult.Unknown, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Single(http.Writes);
    }
    [Fact]
    public async Task Disabling_recovery_during_preflight_prevents_the_patch()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build();
        var http = new Handler(); var token = new TokenSource();
        var adapter = new MicrosoftAccountRecoveryDirectory(configuration, new HttpClient(http), token.Get);
        http.AfterRoleRead = () => configuration["PasswordRecovery:Enabled"] = "false";
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!"));
        Assert.Empty(http.Writes);
    }
    [Fact]
    public async Task A_preflight_failure_cannot_be_confused_with_a_submitted_mutation()
    {
        var (adapter, http, token) = Setup(); token.Fail = true;
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Equal(0, http.Calls);
    }
    [Fact]
    public async Task A_policy_payload_is_not_required_to_be_valid_while_safely_rejecting_unrecognized_errors()
    {
        var (adapter, http, _) = Setup(); http.WriteStatus = HttpStatusCode.BadRequest; http.WriteBody = "invalid JSON";
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable, await adapter.ResetAsync(Account(), "Synthetic-Test-Value-493!")); Assert.Single(http.Writes);
    }
    private sealed class TokenSource
    {
        public int Calls; public string? Changed; public bool Fail;
        public Task<string> Get(CancellationToken ct)
        {
            Calls++; if (Fail) throw new HttpRequestException("Synthetic unavailable credential");
            var claims = new JsonObject
            {
                ["tid"] = (Changed == "tid" ? Guid.Empty : MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId).ToString(),
                ["appid"] = (Changed == "appid" ? Guid.Empty : IdentityClientId).ToString(),
                ["roles"] = new JsonArray(Changed == "roles" ? "User.Read.All" : "User-PasswordProfile.ReadWrite.All")
            };
            static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Task.FromResult(Encode("{\"alg\":\"none\"}") + "." + Encode(claims.ToJsonString()) + ".synthetic-test-signature");
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls, UserReads, RoleReads; public bool LoseWrite, CancelWrite; public Action? AfterRoleRead;
        public JsonObject User = new()
        {
            ["id"] = ObjectId.ToString(), ["userPrincipalName"] = Upn, ["accountEnabled"] = true,
            ["userType"] = "Member", ["onPremisesSyncEnabled"] = false
        };
        public JsonObject Roles = new() { ["value"] = new JsonArray() };
        public HttpStatusCode RoleStatus = HttpStatusCode.OK, WriteStatus = HttpStatusCode.NoContent;
        public string WriteBody = "";
        public List<(string Host, string Path, JsonObject Body)> Writes = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("https", request.RequestUri!.Scheme);
            if (request.Method == HttpMethod.Patch)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                Writes.Add((request.RequestUri.Host, request.RequestUri.AbsolutePath, body));
                if (LoseWrite) throw new HttpRequestException("Synthetic reply lost after write");
                if (CancelWrite) throw new OperationCanceledException("Synthetic cancellation after write");
                return Response(WriteStatus, WriteBody);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri.AbsolutePath.Contains("/transitiveMemberOf")) { RoleReads++; AfterRoleRead?.Invoke(); return Response(RoleStatus, Roles.ToJsonString()); }
            UserReads++; return Response(HttpStatusCode.OK, User.ToJsonString());
        }
        private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
