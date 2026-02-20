namespace EShop.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for OpenTelemetry distributed tracing and metrics.
/// Bound from the "OpenTelemetry" configuration section.
/// </summary>
public class OpenTelemetrySettings
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// OTLP exporter endpoint (e.g., "http://localhost:4317" for gRPC).
    /// When empty, tracing is disabled.
    /// </summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Sampling ratio (0.0 to 1.0). 1.0 = sample everything (dev), 0.1 = 10% (production).
    /// Used with ParentBased(TraceIdRatioBased) sampler.
    /// </summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// Whether tracing is enabled at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether OpenTelemetry metrics with Prometheus exporter are enabled.
    /// When true, OTel metrics (ASP.NET Core, HttpClient, .NET Runtime) are exported
    /// via the Prometheus scraping endpoint alongside existing prometheus-net metrics.
    /// </summary>
    public bool MetricsEnabled { get; set; } = true;
}
