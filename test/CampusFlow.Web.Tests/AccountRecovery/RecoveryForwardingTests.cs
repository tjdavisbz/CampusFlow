using System.Net;
using System.Threading.Tasks;
using CampusFlow.Web.AccountRecovery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Xunit;
namespace CampusFlow.AccountRecoveryTests;
public sealed class RecoveryForwardingTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["PasswordRecovery:TrustedProxyIps:0"]="169.254.129.1",["ASPNETCORE_FORWARDEDHEADERS_ENABLED"]="false" }).Build();
    [Fact] public async Task Verified_edge_forwards_https_and_preserves_the_peer_and_client_chain()
    {
        var options=new ForwardedHeadersOptions();RecoveryClientAddress.ConfigureForwarding(options,Config());
        var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("169.254.129.1");context.Request.Scheme="http";
        context.Request.Headers["X-Forwarded-Proto"]="https";context.Request.Headers["X-Forwarded-For"]="198.51.100.1, 203.0.113.25";
        await new ForwardedHeadersMiddleware(_=>Task.CompletedTask,NullLoggerFactory.Instance,Options.Create(options)).Invoke(context);
        Assert.Equal("https",context.Request.Scheme);Assert.Equal(IPAddress.Parse("169.254.129.1"),context.Connection.RemoteIpAddress);
        Assert.Equal("198.51.100.1, 203.0.113.25",context.Request.Headers["X-Forwarded-For"].ToString());
        Assert.Equal("203.0.113.25",RecoveryClientAddress.Resolve(context,Config()));
        Assert.Equal(ForwardedHeaders.XForwardedProto,options.ForwardedHeaders);Assert.Equal(1,options.ForwardLimit);
        Assert.Single(options.KnownProxies);Assert.Empty(options.KnownIPNetworks);
    }
    [Fact] public async Task Untrusted_peer_cannot_choose_https_or_a_recovery_client_identity()
    {
        var options=new ForwardedHeadersOptions();RecoveryClientAddress.ConfigureForwarding(options,Config());
        var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("198.51.100.1");context.Request.Scheme="http";
        context.Request.Headers["X-Forwarded-Proto"]="https";context.Request.Headers["X-Forwarded-For"]="203.0.113.25";
        await new ForwardedHeadersMiddleware(_=>Task.CompletedTask,NullLoggerFactory.Instance,Options.Create(options)).Invoke(context);
        Assert.Equal("http",context.Request.Scheme);Assert.Null(RecoveryClientAddress.Resolve(context,Config()));
    }
}
