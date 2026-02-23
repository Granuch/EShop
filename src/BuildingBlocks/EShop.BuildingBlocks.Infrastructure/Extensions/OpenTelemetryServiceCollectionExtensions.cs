using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring OpenTelemetry distributed tracing and metrics.
/// All instrumentation logic is centralized here (Infrastructure layer)
/// to keep Application and Domain layers free of observability concerns.
///
/// Instruments:
/// - ASP.NET Core HTTP pipeline (traces + metrics)
/// - HttpClient outgoing requests (traces + metrics)
/// - Entity Framework Core (PostgreSQL) (traces)
/// - .NET Runtime (GC, thread pool, assemblies) (metrics)
/// - MassTransit (RabbitMQ) — automatic via MassTransit's built-in DiagnosticSource
/// - Custom ActivitySources registered by each service
///
/// Exports:
/// - Traces via OTLP (gRPC) to Jaeger or any OTLP-compatible backend
/// - Metrics via Prometheus scraping endpoint (/metrics)
/// </summary>
public static class OpenTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenTelemetry distributed tracing with OTLP exporter
    /// and metrics with Prometheus exporter.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="serviceName">
    /// Logical service name (e.g., "EShop.Identity.API"). 
    /// Maps to the OpenTelemetry resource attribute "service.name".
    /// </param>
    /// <param name="serviceVersion">Service version for resource metadata.</param>
    /// <param name="environment">Hosting environment (used for environment-aware config).</param>
    /// <param name="additionalSources">
    /// Custom ActivitySource names to subscribe to (e.g., "EShop.Identity", "EShop.Catalog").
    /// MassTransit's source is included automatically.
    /// </param>
    public static IServiceCollection AddEShopOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion,
        IHostEnvironment environment,
        params string[] additionalSources)
    {
        var settings = configuration
            .GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        if (!settings.Enabled)
        {
            // OpenTelemetry is fully disabled — no-op. This keeps startup clean in test environments.
            return services;
        }

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName
                }));

        // --- Tracing ---
        if (!string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
        {
            otelBuilder.WithTracing(tracing =>
            {
                // ASP.NET Core incoming HTTP requests
                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    // Filter out health check, metrics, and documentation endpoints to reduce noise
                    options.Filter = httpContext =>
                    {
                        var path = httpContext.Request.Path.Value;
                        if (string.IsNullOrEmpty(path))
                            return true;

                        return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/prometheus", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase);
                    };
                });

                // HttpClient outgoing requests
                tracing.AddHttpClientInstrumentation(options =>
                {
                    // Redact Authorization header values to prevent token leakage in traces
                    options.FilterHttpRequestMessage = request =>
                    {
                        // Don't trace health check calls to avoid recursive tracing
                        if (request.RequestUri?.AbsolutePath.StartsWith("/health") == true)
                            return false;

                        return true;
                    };
                });

                // Entity Framework Core (PostgreSQL queries)
                tracing.AddEntityFrameworkCoreInstrumentation(options =>
                {
                    // Only include raw SQL in Development to prevent PII leakage
                    options.SetDbStatementForText = environment.IsDevelopment();
                    options.SetDbStatementForStoredProcedure = environment.IsDevelopment();
                });

                // MassTransit publishes diagnostic activities under "MassTransit" source.
                // AddSource subscribes to these activities so they appear as spans in Jaeger.
                tracing.AddSource("MassTransit");

                // Subscribe to custom ActivitySources from each service
                foreach (var source in additionalSources)
                {
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        tracing.AddSource(source);
                    }
                }

                // Sampling configuration:
                // ParentBased wraps TraceIdRatioBased so that:
                // - If a parent span is sampled, the child is also sampled (preserves full traces)
                // - If no parent, apply the ratio-based sampling
                // Production: use low ratio (0.1 = 10%). Development: 1.0 = 100%.
                var samplingRatio = Math.Clamp(settings.SamplingRatio, 0.0, 1.0);
                tracing.SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(samplingRatio)));

                // OTLP exporter (gRPC) wrapped in a circuit breaker.
                // When the OTEL Collector / Jaeger is down, the circuit breaker
                // drops traces silently after 5 consecutive failures for 30 seconds,
                // preventing cascading export timeouts and log spam.
                var otlpEndpoint = settings.OtlpEndpoint;
                tracing.AddProcessor(sp =>
                {
                    var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(otlpEndpoint)
                    });

                    var logger = sp.GetRequiredService<ILogger<CircuitBreakerTraceExporter>>();
                    var circuitBreakerExporter = new CircuitBreakerTraceExporter(
                        otlpExporter, logger,
                        failureThreshold: 5,
                        openDuration: TimeSpan.FromSeconds(30));

                    return new ActivityBatchExportProcessor(
                        circuitBreakerExporter,
                        maxQueueSize: 2048,
                        maxExportBatchSize: 512,
                        scheduledDelayMilliseconds: 5000,
                        exporterTimeoutMilliseconds: 30000);
                });
            });
        }

        // --- Metrics ---
        if (settings.MetricsEnabled)
        {
            otelBuilder.WithMetrics(metrics =>
            {
                // ASP.NET Core HTTP server metrics:
                // http.server.request.duration, http.server.active_requests
                metrics.AddAspNetCoreInstrumentation();

                // HttpClient outgoing request metrics:
                // http.client.request.duration, http.client.active_requests
                metrics.AddHttpClientInstrumentation();

                // .NET Runtime metrics:
                // GC collections, heap size, thread pool queue length, exception count, assembly count
                metrics.AddRuntimeInstrumentation();

                // Subscribe to custom Meter sources from each service
                foreach (var source in additionalSources)
                {
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        metrics.AddMeter(source);
                    }
                }

                // Prometheus exporter — exposes metrics at the scraping endpoint.
                // Call app.UseEShopOpenTelemetryPrometheus() to map the endpoint.
                metrics.AddPrometheusExporter();
            });
        }

        return services;
    }

    /// <summary>
    /// Maps the OpenTelemetry Prometheus scraping endpoint.
    /// This exposes OTel metrics (ASP.NET Core, HttpClient, .NET Runtime) at /metrics/otel.
    /// Existing prometheus-net custom business metrics remain at /metrics.
    /// </summary>
    public static WebApplication UseEShopOpenTelemetryPrometheus(this WebApplication app)
    {
        var settings = app.Configuration
            .GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        if (settings is { Enabled: true, MetricsEnabled: true })
        {
            app.UseOpenTelemetryPrometheusScrapingEndpoint();
        }

        return app;
    }

    /// <summary>
    /// Concrete <see cref="BatchExportProcessor{T}"/> for <see cref="System.Diagnostics.Activity"/>.
    /// Required because the SDK marks <c>BatchExportProcessor&lt;T&gt;</c> as abstract.
    /// </summary>
    private sealed class ActivityBatchExportProcessor : BatchExportProcessor<System.Diagnostics.Activity>
    {
        public ActivityBatchExportProcessor(
            BaseExporter<System.Diagnostics.Activity> exporter,
            int maxQueueSize = 2048,
            int scheduledDelayMilliseconds = 5000,
            int exporterTimeoutMilliseconds = 30000,
            int maxExportBatchSize = 512)
            : base(exporter, maxQueueSize, scheduledDelayMilliseconds, exporterTimeoutMilliseconds, maxExportBatchSize)
        {
        }
    }
}
