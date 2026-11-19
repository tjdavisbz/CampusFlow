using System.Collections.Generic;
using System.Net;
using CampusFlow.Web.AccountRecovery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.AccountRecoveryTests;

public sealed class RecoveryClientAddressTests
{
    private static IConfiguration Config(string proxy="10.0.0.5")=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["PasswordRecovery:Backend"]="MicrosoftWriteback",["PasswordRecovery:TrustedProxyIps:0"]=proxy }).Build();
    [Theory]
    [InlineData("203.0.113.25","203.0.113.25")]
    [InlineData("198.51.100.1, 203.0.113.25","203.0.113.25")]
    [InlineData("198.51.100.1, 203.0.113.25:54321","203.0.113.25")]
    [InlineData("2001:db8::1","2001:db8::1")]
    public void Only_the_immediate_trusted_proxy_can_supply_the_client_identity(string header,string expected)
    {var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("10.0.0.5");context.Request.Headers["X-Forwarded-For"]=header;Assert.Equal(expected,RecoveryClientAddress.Resolve(context,Config()));}
    [Theory][InlineData("")][InlineData("not-an-address")][InlineData("203.0.113.25,")][InlineData("0.0.0.0")][InlineData("::")]
    public void Missing_or_invalid_forwarded_identity_is_unavailable(string header)
    {var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("10.0.0.5");context.Request.Headers["X-Forwarded-For"]=header;Assert.Null(RecoveryClientAddress.Resolve(context,Config()));}
    [Fact]public void Untrusted_peer_cannot_spoof_a_rate_limit_identity()
    {var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse("198.51.100.2");context.Request.Headers["X-Forwarded-For"]="203.0.113.25";Assert.Null(RecoveryClientAddress.Resolve(context,Config()));}
    [Theory][InlineData("0.0.0.0")][InlineData("::")][InlineData("10.0.0.0/8")][InlineData("*")][InlineData("")]
    public void Wildcard_or_unknown_proxy_configuration_is_rejected(string proxy)=>Assert.False(RecoveryClientAddress.HasConfiguration(Config(proxy)));
    [Fact]public void Automatic_unrestricted_forwarding_cannot_replace_the_verified_peer()
    {var config=(IConfigurationRoot)Config();config["ASPNETCORE_FORWARDEDHEADERS_ENABLED"]="true";Assert.False(RecoveryClientAddress.HasConfiguration(config));}
}
