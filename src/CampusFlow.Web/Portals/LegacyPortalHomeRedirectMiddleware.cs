using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CampusFlow.Web.Portals;

/// <summary>Keep links to the retired ASP portal's home page working.</summary>
public sealed class LegacyPortalHomeRedirectMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
            context.Request.Path.Equals("/index.asp", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy query parameters have no meaning in CampusFlow and may contain old login data.
            context.Response.Redirect("/", permanent: false);
            return Task.CompletedTask;
        }

        return next(context);
    }
}
