using System;
using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CampusFlow.Web.Maintenance;

public sealed class MaintenanceModeMiddleware
{
    private const string DefaultDescription =
        "CampusFlow is temporarily unavailable while scheduled maintenance is completed. Please check back soon.";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<MaintenanceModeOptions> _options;

    public MaintenanceModeMiddleware(
        RequestDelegate next,
        IOptionsMonitor<MaintenanceModeOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || IsAllowedDuringMaintenance(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var description = string.IsNullOrWhiteSpace(options.Description)
            ? DefaultDescription
            : options.Description.Trim();

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.RetryAfter = "300";
        context.Response.Headers.Append("X-Robots-Tag", "noindex, nofollow");

        await context.Response.WriteAsync(BuildPage(description), context.RequestAborted);
    }

    internal static bool IsAllowedDuringMaintenance(PathString path) =>
        path.StartsWithSegments("/health-status") ||
        path.StartsWithSegments("/healthchecks-ui") ||
        path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/fonts") ||
        path.StartsWithSegments("/favicon") ||
        path.Value?.EndsWith(".css", StringComparison.OrdinalIgnoreCase) == true ||
        path.Value?.EndsWith(".js", StringComparison.OrdinalIgnoreCase) == true ||
        path.Value?.EndsWith(".map", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildPage(string description)
    {
        var encodedDescription = HtmlEncoder.Default.Encode(description)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="robots" content="noindex,nofollow">
                <title>CampusFlow maintenance | Nelson University</title>
                <style>
                    :root { color-scheme: light; --purple: #502d7f; --dark-purple: #28163f; --lavender: #c5c0e0; }
                    * { box-sizing: border-box; }
                    body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 2rem; background: linear-gradient(145deg, var(--dark-purple), var(--purple)); color: #17121f; font-family: "Nunito Sans", Arial, sans-serif; }
                    main { width: min(44rem, 100%); overflow: hidden; border-radius: 1.25rem; background: #fff; box-shadow: 0 1.5rem 4rem rgba(0,0,0,.28); }
                    header { display: flex; align-items: center; justify-content: center; min-height: 10rem; padding: 2rem; background: var(--purple); }
                    header img { width: min(17rem, 75%); height: auto; }
                    section { padding: clamp(2rem, 6vw, 3.5rem); text-align: center; }
                    .icon { display: grid; place-items: center; width: 4rem; height: 4rem; margin: 0 auto 1.25rem; border-radius: 50%; background: #f1edf7; color: var(--purple); font-size: 2rem; font-weight: 800; }
                    h1 { margin: 0 0 1rem; color: var(--dark-purple); font-family: "Noto Serif", Georgia, serif; font-size: clamp(2rem, 7vw, 3rem); line-height: 1.1; }
                    p { margin: 0 auto; max-width: 36rem; color: #4f4758; font-size: 1.1rem; line-height: 1.7; }
                    .note { margin-top: 2rem; padding-top: 1.5rem; border-top: 1px solid #e7e2ec; color: #6d6475; font-size: .95rem; }
                </style>
            </head>
            <body>
                <main role="main" aria-labelledby="maintenance-title">
                    <header>
                        <img src="/images/tenants/nelson/logo-stacked-reverse.png" alt="Nelson University">
                    </header>
                    <section>
                        <div class="icon" aria-hidden="true">!</div>
                        <h1 id="maintenance-title">We’ll be back soon</h1>
                        <p>{{encodedDescription}}</p>
                        <p class="note">Thank you for your patience.</p>
                    </section>
                </main>
            </body>
            </html>
            """;
    }
}
