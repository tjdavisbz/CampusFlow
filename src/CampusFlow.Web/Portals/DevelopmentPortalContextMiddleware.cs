using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.Portals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace CampusFlow.Web.Portals;

public class DevelopmentPortalContextMiddleware
{
    public const string PortalItemKey = "CampusFlow.PortalType";

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;

    public DevelopmentPortalContextMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_environment.IsDevelopment())
        {
            ApplyDevelopmentContext(context);
        }

        await _next(context);
    }

    private static void ApplyDevelopmentContext(HttpContext context)
    {
        var tenant = context.Request.Query["tenant"].ToString();
        var portal = context.Request.Query["portal"].ToString();

        if (!string.IsNullOrWhiteSpace(tenant))
        {
            var query = new List<KeyValuePair<string, string?>>();
            foreach (var item in context.Request.Query)
            {
                foreach (var value in item.Value)
                {
                    query.Add(new KeyValuePair<string, string?>(item.Key, value));
                }
            }

            query.Add(new KeyValuePair<string, string?>("__tenant", tenant));
            context.Request.QueryString = QueryString.Create(query);
        }

        if (Enum.TryParse<PortalType>(portal, ignoreCase: true, out var portalType))
        {
            context.Items[PortalItemKey] = portalType;
        }
    }
}
