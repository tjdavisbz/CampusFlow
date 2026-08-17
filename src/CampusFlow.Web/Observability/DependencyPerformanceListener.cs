using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace CampusFlow.Web.Observability;

public sealed class DependencyPerformanceListener : IHostedService, IDisposable
{
    private readonly ILogger<DependencyPerformanceListener> _logger;
    private readonly PerformanceLoggingOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private ActivityListener? _listener;

    public DependencyPerformanceListener(
        ILogger<DependencyPerformanceListener> logger,
        IOptions<PerformanceLoggingOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
                source.Name.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ||
                source.Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = RecordDependency
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void RecordDependency(Activity activity)
    {
        var isHttp = activity.Source.Name.Contains("Http", StringComparison.OrdinalIgnoreCase);
        if (_httpContextAccessor.HttpContext?.Items[typeof(RequestDependencyMetrics)]
            is RequestDependencyMetrics requestMetrics)
        {
            requestMetrics.Add(isHttp, activity.Duration.TotalMilliseconds);
        }

        if (activity.Duration.TotalMilliseconds < _options.SlowDependencyMilliseconds) return;

        var dependencyType = isHttp ? "HTTP" : "SQL";
        var target = isHttp ? GetHttpTarget(activity) : GetDatabaseTarget(activity);
        var status = activity.Status == ActivityStatusCode.Error ? "Error" : "Completed";

        _logger.LogWarning(
            "Slow {DependencyType} dependency {DependencyOperation} to {DependencyTarget} {DependencyStatus} in {ElapsedMilliseconds:F0} ms. TraceId={TraceId}",
            dependencyType, activity.DisplayName, target, status,
            activity.Duration.TotalMilliseconds, activity.TraceId.ToString());
    }

    private static string GetHttpTarget(Activity activity)
    {
        var host = activity.GetTagItem("server.address")?.ToString();
        if (!string.IsNullOrWhiteSpace(host)) return host;

        var url = activity.GetTagItem("url.full")?.ToString();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "external-service";
    }

    private static string GetDatabaseTarget(Activity activity)
    {
        var system = activity.GetTagItem("db.system.name")?.ToString()
                     ?? activity.GetTagItem("db.system")?.ToString()
                     ?? "database";
        var database = activity.GetTagItem("db.namespace")?.ToString()
                       ?? activity.GetTagItem("db.name")?.ToString();
        return string.IsNullOrWhiteSpace(database) ? system : $"{system}/{database}";
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
