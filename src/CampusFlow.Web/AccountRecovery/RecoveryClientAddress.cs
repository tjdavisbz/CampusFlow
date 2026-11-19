using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.AccountRecovery;

internal static class RecoveryClientAddress
{
    internal static bool RequiresTrustedProxy(IConfiguration? config) => config?["PasswordRecovery:Backend"] == "MicrosoftWriteback";
    internal static bool HasConfiguration(IConfiguration config) => Trusted(config).Length > 0;
    private static IPAddress[] Trusted(IConfiguration config)
    {
        var automaticForwarding = config["ASPNETCORE_FORWARDEDHEADERS_ENABLED"];
        if (!string.IsNullOrEmpty(automaticForwarding) && !string.Equals(automaticForwarding, "false", StringComparison.OrdinalIgnoreCase)) return [];
        var entries = config.GetSection("PasswordRecovery:TrustedProxyIps").GetChildren().Select(x => x.Value).ToArray();
        if (entries.Length is 0 or > 16) return [];
        var addresses = entries.Select(x => IPAddress.TryParse(x, out var ip) ? Normalize(ip) : null).ToArray();
        return addresses.Any(x => x is null || x.Equals(IPAddress.Any) || x.Equals(IPAddress.IPv6Any)) ? [] : addresses.Select(x => x!).ToArray();
    }
    internal static string? Resolve(HttpContext context, IConfiguration config)
    {
        var peer = context.Connection.RemoteIpAddress;
        if (peer is null || !Trusted(config).Contains(Normalize(peer))) return null;
        var chain = context.Request.Headers["X-Forwarded-For"].ToString().Split(',');
        if (chain.Length is 0 or > 8) return null;
        // The immediate trusted proxy appends its observed client at the right.
        // User-supplied entries at the left cannot choose the rate-limit identity.
        var value = chain[^1].Trim();
        if (!IPAddress.TryParse(value, out var address))
        {
            if (!IPEndPoint.TryParse(value, out var endpoint)) return null;
            address = endpoint.Address;
        }
        address = Normalize(address);
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? null : address.ToString();
    }
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
