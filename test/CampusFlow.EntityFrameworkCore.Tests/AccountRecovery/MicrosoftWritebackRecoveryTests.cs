using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.AccountRecovery;

public sealed class MicrosoftWritebackRecoveryTests
{
    private const string Upn = "synthetic@my.nelson.edu";
    private static readonly Guid Student = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Service = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid Client = Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa");
    private static RecoveryDirectoryAccount Account() => new(Student, Upn, true, true, false, true);
    internal static Dictionary<string,string?> Settings() => new()
    {
        ["PasswordRecovery:Backend"]="MicrosoftWriteback", ["PasswordRecovery:Enabled"]="true", ["PasswordRecovery:EnableNonProductionWrites"]="true",
        ["PasswordRecovery:EnableProductionWrites"]="false", ["PasswordRecovery:DirectoryTenantId"]=MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId.ToString("D"),
        ["PasswordRecovery:DelegatedClientId"]=Client.ToString("D"), ["PasswordRecovery:ServiceAccount:ObjectId"]=Service.ToString("D"),
        ["PasswordRecovery:ServiceAccount:Username"]="synthetic.service@nelson.edu", ["PasswordRecovery:TokenEncryptionKey"]=Convert.ToBase64String(new byte[32]),
        ["PasswordRecovery:AllowedStudentUpns:0"]=Upn, ["ASPNETCORE_ENVIRONMENT"]="Staging", ["App:EnvironmentLabel"]="NON-PRODUCTION",
        ["App:SelfUrl"]="https://my-np.nelson.edu", ["App:DisablePII"]="true"
    };
    private static IConfigurationRoot Configuration(Dictionary<string,string?>? settings=null) => new ConfigurationBuilder().AddInMemoryCollection(settings??Settings()).Build();
    private static RecoveryAccessToken Token(string? changed=null) => new("opaque-graph-token",
        changed=="tid" ? Guid.NewGuid() : MicrosoftAccountRecoveryDirectory.NelsonDirectoryTenantId,
        changed=="oid" ? Guid.NewGuid() : Service, "synthetic.service@nelson.edu",
        changed=="appid" ? Guid.NewGuid() : Client,
        changed=="aud" ? "https://untrusted.invalid" : "https://graph.microsoft.com",
        changed=="app-only" ? [] : changed=="scp" ? ["User.Read.All"] : ["User.Read.All", "UserAuthenticationMethod.ReadWrite.All"],
        DateTimeOffset.UtcNow.AddMinutes(changed=="exp" ? -1 : 60));
    private static (MicrosoftWritebackAccountRecoveryDirectory Adapter, Handler Http) Setup(IConfiguration? config=null,string? changedToken=null,bool authorized=true)
    {
        var http=new Handler();return(new(config??Configuration(),new HttpClient(http),_=>Task.FromResult(Token(changedToken)),()=>authorized,(_,_)=>Task.CompletedTask),http);
    }
    [Fact] public async Task Synced_student_is_reset_once_and_running_operation_is_polled_to_success()
    {
        var(a,h)=Setup();h.States.Enqueue("running");h.States.Enqueue("succeeded");Assert.True(a.SupportsSynchronizedAccounts);Assert.Equal(Account(),await a.FindAsync(Upn));
        Assert.Equal(RecoveryPasswordWriteResult.Accepted,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(1,h.Writes);Assert.Equal(2,h.Polls);
        Assert.EndsWith("/authentication/methods/28c10230-6103-485e-b985-444c60001490/resetPassword",h.WritePath);
        Assert.Equal("newPassword",Assert.Single(h.Body!).Key);Assert.Equal("Synthetic-Password-947!",h.Body!["newPassword"]!.GetValue<string>());
    }
    [Fact]
    public async Task A_browser_disconnect_after_submission_does_not_cancel_confirmation_or_send_again()
    {
        using var browser = new CancellationTokenSource();
        var (adapter, http) = Setup(); http.AfterWrite = () => browser.Cancel();
        http.States.Enqueue("running"); http.States.Enqueue("succeeded");
        Assert.Equal(RecoveryPasswordWriteResult.Accepted, await adapter.ResetAsync(Account(), "Synthetic-Password-947!", browser.Token));
        Assert.Equal(1, http.Writes); Assert.Equal(2, http.Polls);
    }
    [Theory]
    [InlineData("?aadgdc=DUB02P&aadgsu=ssprprod-a")]
    [InlineData("?aadgsu=ssprprod-a&aadgdc=DUB02P")]
    [InlineData("?aadgdc=AM6")]
    [InlineData("?aadgsu=ssprprod-a")]
    public async Task Documented_status_routing_is_preserved_and_confirmation_never_resubmits(string query)
    {
        var(a,h)=Setup();h.Location+=query;h.States.Enqueue("running");h.States.Enqueue("succeeded");
        Assert.Equal(RecoveryPasswordWriteResult.Accepted,await a.ResetAsync(Account(),"Synthetic-Password-947!"));
        Assert.Equal(1,h.Writes);Assert.Equal(2,h.Polls);Assert.Equal(query,h.PollQuery);
    }
    [Theory]
    [InlineData("?token=secret")][InlineData("?aadgdc=A&aadgdc=B")][InlineData("?aadgsu=x&redirect_uri=https://untrusted.invalid")]
    [InlineData("?aadgdc=")][InlineData("?aadgdc=%0d%0aHost:evil")][InlineData("?aadgsu=https%3A%2F%2Fevil.invalid")]
    [InlineData("?AADGDC=A")][InlineData("?aadgdc=A&")]
    public async Task Unrecognized_or_unsafe_routing_never_triggers_a_status_request(string query)
    {
        var(a,h)=Setup();h.Location+=query;
        Assert.Equal(RecoveryPasswordWriteResult.Unknown,await a.ResetAsync(Account(),"Synthetic-Password-947!"));
        Assert.Equal(1,h.Writes);Assert.Equal(0,h.Polls);
    }
    [Theory]
    [InlineData("PasswordPolicyError")]
    [InlineData("{\"error\":{\"code\":\"PasswordPolicyError\"}}")]
    public async Task A_failed_operation_with_a_confirmed_policy_code_is_retryable(string detail)
    {
        var (adapter, http) = Setup(); http.FallbackState = "failed"; http.StatusDetail = detail;
        Assert.Equal(RecoveryPasswordWriteResult.PolicyRejected, await adapter.ResetAsync(Account(), "Synthetic-Password-947!"));
        Assert.Equal(1, http.Writes); Assert.Equal(1, http.Polls);
    }
    [Fact]
    public async Task A_direct_policy_failure_is_not_reported_as_an_unconfirmed_reset()
    {
        var (adapter, http) = Setup(); http.WriteStatus = HttpStatusCode.BadRequest; http.ErrorCode = "PasswordPolicyError";
        Assert.Equal(RecoveryPasswordWriteResult.PolicyRejected, await adapter.ResetAsync(Account(), "Synthetic-Password-947!"));
        Assert.Equal(1, http.Writes); Assert.Equal(0, http.Polls);
    }
    [Theory]
    [InlineData("UnknownPasswordPolicyErrorCode")]
    [InlineData("Upstream service unavailable")]
    public async Task An_unrecognized_failure_does_not_release_the_reset_hold(string detail)
    {
        var (adapter, http) = Setup(); http.FallbackState = "failed"; http.StatusDetail = detail;
        Assert.Equal(RecoveryPasswordWriteResult.Unknown, await adapter.ResetAsync(Account(), "Synthetic-Password-947!"));
        Assert.Equal(1, http.Writes);
    }
    [Theory]
    [InlineData("PasswordRecovery:Enabled","false")][InlineData("PasswordRecovery:Backend","Cloud")]
    [InlineData("PasswordRecovery:EnableNonProductionWrites","false")][InlineData("PasswordRecovery:EnableProductionWrites","true")]
    [InlineData("ASPNETCORE_ENVIRONMENT","Production")]
    [InlineData("App:DisablePII","false")][InlineData("ASPNETCORE_ENVIRONMENT","Development")]
    [InlineData("App:EnvironmentLabel","unrecognized")][InlineData("App:SelfUrl","https://my.nelson.edu")]
    [InlineData("PasswordRecovery:DirectoryTenantId","aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [InlineData("PasswordRecovery:DelegatedClientId","")][InlineData("PasswordRecovery:ServiceAccount:ObjectId","")]
    [InlineData("PasswordRecovery:ServiceAccount:Username","other@outside.invalid")][InlineData("PasswordRecovery:TokenEncryptionKey","")]
    [InlineData("PasswordRecovery:AllowedStudentUpns:0","other@outside.invalid")]
    public async Task Missing_or_cross_environment_configuration_never_calls_graph(string key,string value)
    {var settings=Settings();settings[key]=value;var(a,h)=Setup(Configuration(settings));Assert.False(a.IsConfigured);Assert.False(a.SupportsSynchronizedAccounts);Assert.Null(await a.FindAsync(Upn));Assert.Equal(RecoveryPasswordWriteResult.Unavailable,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(0,h.Calls);}
    [Fact]public async Task Missing_operator_authorization_never_calls_graph()
    {var(a,h)=Setup(authorized:false);Assert.False(a.IsConfigured);Assert.Null(await a.FindAsync(Upn));Assert.Equal(RecoveryPasswordWriteResult.Unavailable,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(0,h.Calls);}
    [Theory][InlineData("tid")][InlineData("oid")][InlineData("appid")][InlineData("aud")][InlineData("scp")][InlineData("exp")][InlineData("app-only")]
    public async Task Wrong_service_identity_scope_tenant_client_audience_or_expiration_never_reaches_graph(string claim)
    {var(a,h)=Setup(changedToken:claim);Assert.Null(await a.FindAsync(Upn));Assert.Equal(RecoveryPasswordWriteResult.Unavailable,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(0,h.Calls);}
    [Theory][InlineData("disabled")][InlineData("guest")][InlineData("privileged")][InlineData("unknown-sync")][InlineData("wrong-id")][InlineData("roles-unreadable")][InlineData("roles-next-page")]
    public async Task Target_status_and_privilege_are_rechecked_before_submission(string condition)
    {
        var(a,h)=Setup();if(condition=="disabled")h.User["accountEnabled"]=false;if(condition=="guest")h.User["userType"]="Guest";
        if(condition=="privileged")h.Roles["value"]=new JsonArray(new JsonObject{["@odata.type"]="#microsoft.graph.directoryRole",["id"]=Guid.NewGuid().ToString()});
        if(condition=="unknown-sync")h.User.Remove("onPremisesSyncEnabled");if(condition=="wrong-id")h.User["id"]=Guid.NewGuid().ToString();
        if(condition=="roles-unreadable")h.RoleStatus=HttpStatusCode.Forbidden;if(condition=="roles-next-page")h.Roles["@odata.nextLink"]="https://untrusted.invalid";
        Assert.Equal(RecoveryPasswordWriteResult.Unavailable,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(0,h.Writes);
    }
    [Theory][InlineData("loss")][InlineData("cancel")][InlineData("missing-location")][InlineData("foreign-location")][InlineData("wrong-student")][InlineData("poll-failure")][InlineData("failed")][InlineData("unknown")][InlineData("running")][InlineData("mismatched-operation")]
    public async Task Unconfirmed_outcomes_never_report_success_or_resubmit(string condition)
    {
        var(a,h)=Setup();h.LoseWrite=condition=="loss";h.CancelWrite=condition=="cancel";
        if(condition=="missing-location")h.Location=null;if(condition=="foreign-location")h.Location="https://untrusted.invalid/operation";
        if(condition=="wrong-student")h.Location=h.Location!.Replace(Student.ToString("D"),Service.ToString("D"));
        if(condition=="poll-failure")h.PollStatus=HttpStatusCode.Forbidden;
        if(condition is "failed" or "unknown" or "running")h.FallbackState=condition;
        if(condition=="mismatched-operation")h.OperationId="different-operation";
        Assert.Equal(RecoveryPasswordWriteResult.Unknown,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(1,h.Writes);
        if(condition=="running")Assert.Equal(12,h.Polls);
        if(condition is "foreign-location" or "wrong-student" or "missing-location")Assert.Equal(0,h.Polls);
    }
    [Theory][InlineData(400,RecoveryPasswordWriteResult.PolicyRejected)][InlineData(401,RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(403,RecoveryPasswordWriteResult.Unavailable)][InlineData(429,RecoveryPasswordWriteResult.Unavailable)]
    [InlineData(408,RecoveryPasswordWriteResult.Unknown)][InlineData(500,RecoveryPasswordWriteResult.Unknown)][InlineData(204,RecoveryPasswordWriteResult.Unknown)]
    public async Task Microsoft_policy_is_authoritative_and_accepted_does_not_mean_completed(int code,RecoveryPasswordWriteResult expected)
    {var(a,h)=Setup();h.WriteStatus=(HttpStatusCode)code;Assert.Equal(expected,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(1,h.Writes);Assert.Equal(0,h.Polls);}
    [Fact]public async Task Disabling_during_preflight_prevents_the_post()
    {var config=Configuration();var(a,h)=Setup(config);h.AfterRoleRead=()=>config["PasswordRecovery:Enabled"]="false";Assert.Equal(RecoveryPasswordWriteResult.Unavailable,await a.ResetAsync(Account(),"Synthetic-Password-947!"));Assert.Equal(0,h.Writes);}
    [Theory]
    [InlineData("http://graph.microsoft.com/v1.0/users/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/authentication/operations/op")]
    [InlineData("https://graph.microsoft.com.evil.invalid/v1.0/users/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/authentication/operations/op")]
    [InlineData("https://graph.microsoft.com/v1.0/users/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/authentication/operations/op?token=x")]
    [InlineData("https://graph.microsoft.com/v1.0/users/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/authentication/operations/op#fragment")]
    [InlineData("https://graph.microsoft.com/v1.0/users/bbbbbbbb-cccc-dddd-eeee-ffffffffffff/authentication/operations/op")]
    [InlineData("https://graph.microsoft.com/v1.0/me/authentication/operations/op")]
    [InlineData("/v1.0/users/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/authentication/operations/op")]
    public void Operation_status_url_cannot_change_host_student_or_path(string location)=>Assert.False(MicrosoftWritebackAccountRecoveryDirectory.ValidOperation(new Uri(location,UriKind.RelativeOrAbsolute),Student));

    private sealed class Handler:HttpMessageHandler
    {
        internal int Calls,Writes,Polls;internal string? WritePath,PollQuery;internal JsonObject? Body;internal bool LoseWrite,CancelWrite;
        internal string? Location="https://graph.microsoft.com/v1.0/users/"+Student.ToString("D")+"/authentication/operations/synthetic-op";
        internal string? StatusDetail; internal string ErrorCode="PasswordTooWeak";
        internal string OperationId="synthetic-op",FallbackState="succeeded";internal Queue<string> States=new();
        internal HttpStatusCode WriteStatus=HttpStatusCode.Accepted,RoleStatus=HttpStatusCode.OK,PollStatus=HttpStatusCode.OK;
        internal Action? AfterRoleRead, AfterWrite;
        internal JsonObject User=new(){["id"]=Student.ToString("D"),["userPrincipalName"]=Upn,["accountEnabled"]=true,["userType"]="Member",["onPremisesSyncEnabled"]=true};
        internal JsonObject Roles=new(){["value"]=new JsonArray()};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Assert.Equal("graph.microsoft.com",request.RequestUri!.Host);
            if(request.Method==HttpMethod.Post)
            {
                Writes++;WritePath=request.RequestUri.AbsolutePath;Body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                if(LoseWrite)throw new HttpRequestException("Synthetic lost response");if(CancelWrite)throw new OperationCanceledException();
                var response=Response(WriteStatus,new(){["error"]=new JsonObject{["code"]=ErrorCode,["message"]="Synthetic policy rejection"}});
                if(Location is not null)response.Headers.Location=new Uri(Location);AfterWrite?.Invoke();return response;
            }
            if(request.RequestUri.AbsolutePath.Contains("/authentication/operations/")){Polls++;PollQuery=request.RequestUri.Query;return Response(PollStatus,new(){["id"]=OperationId,["status"]=States.Count>0?States.Dequeue():FallbackState,["statusDetail"]=StatusDetail});}
            if(request.RequestUri.AbsolutePath.EndsWith("/transitiveMemberOf")){AfterRoleRead?.Invoke();return Response(RoleStatus,Roles);}
            return Response(HttpStatusCode.OK,User);
        }
        private static HttpResponseMessage Response(HttpStatusCode code,JsonObject body)=>new(code){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
    }
}

public sealed class RecoveryTokenCacheTests
{

    [Fact] public async Task Encrypted_operator_bootstrap_is_bound_and_becomes_a_private_runtime_cache()
    {
        var root=Path.Combine(Path.GetTempPath(),"cf028-bootstrap-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var values=MicrosoftWritebackRecoveryTests.Settings();var settings=new WritebackRecoverySettings(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            var plain=Encoding.UTF8.GetBytes("synthetic-session-not-a-real-token");var first=new RecoveryTokenCache(settings,Path.Combine(root,"operator"));
            using(await first.LockAsync(default))await first.WriteAsync(plain,default);
            var blob=await File.ReadAllBytesAsync(Path.Combine(root,"operator","nonprod",settings.KeyId,"delegated-cache.bin"));
            values["PasswordRecovery:BootstrapAuthorizationCache"]=Convert.ToBase64String(blob);
            var runtime=new RecoveryTokenCache(new(new ConfigurationBuilder().AddInMemoryCollection(values).Build()),Path.Combine(root,"runtime"));Assert.True(runtime.Exists);
            using(await runtime.LockAsync(default)){Assert.Equal(plain,await runtime.ReadAsync(default));await runtime.WriteAsync(plain,default);Assert.Equal(plain,await runtime.ReadAsync(default));}
            values["PasswordRecovery:ServiceAccount:ObjectId"]=Guid.NewGuid().ToString();
            var wrong=new RecoveryTokenCache(new(new ConfigurationBuilder().AddInMemoryCollection(values).Build()),Path.Combine(root,"wrong"));await Assert.ThrowsAsync<AuthenticationTagMismatchException>(()=>wrong.ReadAsync(default));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public async Task Cache_is_encrypted_owner_only_and_bound_to_environment_account_client_and_key()
    {
        var root=Path.Combine(Path.GetTempPath(),"cf028-cache-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var values=MicrosoftWritebackRecoveryTests.Settings();var settings=new WritebackRecoverySettings(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            var cache=new RecoveryTokenCache(settings,root);var plain=Encoding.UTF8.GetBytes("synthetic-msal-cache-with-no-real-token");
            using(await cache.LockAsync(default)){await cache.WriteAsync(plain,default);Assert.Equal(plain,await cache.ReadAsync(default));}
            var file=Path.Combine(root,"nonprod",settings.KeyId,"delegated-cache.bin");Assert.DoesNotContain(Encoding.UTF8.GetString(plain),Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)));
            if(!OperatingSystem.IsWindows())Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(file));
            foreach(var key in new[]{"PasswordRecovery:ServiceAccount:ObjectId","PasswordRecovery:DelegatedClientId","PasswordRecovery:TokenEncryptionKey","PasswordRecovery:ServiceAccount:Username"})
            {
                var changed=new Dictionary<string,string?>(values);changed[key]=key.EndsWith("Key")?Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)):key.EndsWith("Username")?"another.service@nelson.edu":Guid.NewGuid().ToString();
                var other=new RecoveryTokenCache(new(new ConfigurationBuilder().AddInMemoryCollection(changed).Build()),root);
                if(key.EndsWith("Key")) await Assert.ThrowsAsync<CryptographicException>(()=>other.ReadAsync(default));
                else await Assert.ThrowsAsync<AuthenticationTagMismatchException>(()=>other.ReadAsync(default));
            }
            var data=await File.ReadAllBytesAsync(file);data[^1]^=1;await File.WriteAllBytesAsync(file,data);await Assert.ThrowsAsync<AuthenticationTagMismatchException>(()=>cache.ReadAsync(default));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
