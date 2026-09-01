using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.Portals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CampusFlow.Web.Portals;

public class DevelopmentPortalContextMiddleware
{
    public const string PortalItemKey = "CampusFlow.PortalType";
    private const string TenantCookieName = "CampusFlow.DevelopmentTenant";

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public DevelopmentPortalContextMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _next = next;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_environment.IsDevelopment())
        {
            ApplyDevelopmentContext(context);
        }
        else
        {
            ApplyConfiguredTenant(context);
        }

        await _next(context);
    }

    private void ApplyConfiguredTenant(HttpContext context)
    {
        var tenant = _configuration["TenantResolution:DefaultTenant"];
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            SetTenantQueryParameter(context, tenant);
        }
    }

    private void ApplyDevelopmentContext(HttpContext context)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        if (string.IsNullOrWhiteSpace(tenant))
        {
            tenant = context.Request.Query["__tenant"].ToString();
        }
        if (string.IsNullOrWhiteSpace(tenant))
        {
            tenant = context.Request.Cookies[TenantCookieName] ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(tenant))
        {
            tenant = _configuration["DevelopmentPortal:DefaultTenant"] ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(tenant))
        {
            context.Response.Cookies.Append(TenantCookieName, tenant, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps
            });
        }
        var portal = context.Request.Query["portal"].ToString();

        if (!string.IsNullOrWhiteSpace(tenant)) SetTenantQueryParameter(context, tenant);

        // Keep the legacy development URL working while the product-facing name moves
        // from Scheduler Portal to Advisor Portal.
        if (portal.Equals("scheduler", StringComparison.OrdinalIgnoreCase))
        {
            portal = nameof(PortalType.Advisor);
        }

        if (Enum.TryParse<PortalType>(portal, ignoreCase: true, out var portalType))
        {
            context.Items[PortalItemKey] = portalType;
        }
    }

    private static void SetTenantQueryParameter(HttpContext context, string tenant)
    {
        var query = new List<KeyValuePair<string, string?>>();
        foreach (var item in context.Request.Query)
        {
            if (item.Key.Equals("__tenant", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var value in item.Value)
            {
                query.Add(new KeyValuePair<string, string?>(item.Key, value));
            }
        }

        query.Add(new KeyValuePair<string, string?>("__tenant", tenant));
        context.Request.QueryString = QueryString.Create(query);
    }
}
