namespace CampusFlow.Web.Observability;

public sealed class PerformanceLoggingOptions
{
    public const string SectionName = "PerformanceLogging";

    public int SlowRequestMilliseconds { get; set; } = 1000;
    public int SlowDependencyMilliseconds { get; set; } = 500;
}
