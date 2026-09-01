using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusFlow.Web.Observability;

public sealed class RequestPerformanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestPerformanceMiddleware> _logger;
    private readonly PerformanceLoggingOptions _options;

    public RequestPerformanceMiddleware(
        RequestDelegate next,
        ILogger<RequestPerformanceMiddleware> logger,
        IOptions<PerformanceLoggingOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var dependencies = new RequestDependencyMetrics();
        context.Items[typeof(RequestDependencyMetrics)] = dependencies;
        try
        {
            await _next(context);
        }
        finally
        {
            if (!IsAssetRequest(context.Request.Path))
                RecordRequest(context, started, dependencies);
        }
    }

    private void RecordRequest(HttpContext context, long started, RequestDependencyMetrics dependencies)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        var route = context.GetEndpoint()?.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName
                    ?? context.GetEndpoint()?.DisplayName
                    ?? context.Request.Path.Value
                    ?? "/";
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (elapsed.TotalMilliseconds >= _options.SlowRequestMilliseconds)
        {
            _logger.LogWarning(
                "Slow request {RequestMethod} {RequestRoute} returned {StatusCode} in {ElapsedMilliseconds:F0} ms " +
                "(SQL: {SqlMilliseconds:F0} ms across {SqlCalls} calls; HTTP: {HttpMilliseconds:F0} ms across {HttpCalls} calls). TraceId={TraceId}",
                context.Request.Method, route, context.Response.StatusCode, elapsed.TotalMilliseconds,
                dependencies.SqlMilliseconds, dependencies.SqlCalls,
                dependencies.HttpMilliseconds, dependencies.HttpCalls, traceId);
        }
        else
        {
            _logger.LogDebug(
                "Request {RequestMethod} {RequestRoute} returned {StatusCode} in {ElapsedMilliseconds:F0} ms. TraceId={TraceId}",
                context.Request.Method, route, context.Response.StatusCode, elapsed.TotalMilliseconds, traceId);
        }
    }

    private static bool IsAssetRequest(PathString path) =>
        path.StartsWithSegments("/libs") ||
        path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/fonts") ||
        path.StartsWithSegments("/favicon") ||
        path.Value?.EndsWith(".css", System.StringComparison.OrdinalIgnoreCase) == true ||
        path.Value?.EndsWith(".js", System.StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class RequestDependencyMetrics
{
    private readonly object _sync = new();
    public double SqlMilliseconds { get; private set; }
    public double HttpMilliseconds { get; private set; }
    public int SqlCalls { get; private set; }
    public int HttpCalls { get; private set; }

    public void Add(bool isHttp, double elapsedMilliseconds)
    {
        lock (_sync)
        {
            if (isHttp)
            {
                HttpCalls++;
                HttpMilliseconds += elapsedMilliseconds;
            }
            else
            {
                SqlCalls++;
                SqlMilliseconds += elapsedMilliseconds;
            }
        }
    }
}
