using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The response writer for every health endpoint in every component.
///
/// It exists to replace <c>UIResponseWriter.WriteHealthCheckUIResponse</c>, which serialises each
/// check's <c>Description</c>, its <c>Data</c> dictionary <b>and its exception message</b>. All
/// three health endpoints are anonymous, so on an unhealthy service that handed any caller the
/// database host and username, the Redis endpoint, and raw connection errors — a free map of the
/// internal topology, returned precisely when the service is in trouble (SEC-07).
///
/// What a probe actually needs is which check failed, not why: the "why" belongs in the server
/// log, where it is already written. So the body is deliberately reduced to the overall status,
/// how long the checks took, and a name/status pair per check.
///
/// Note the status code is still set by the health middleware from the overall
/// <see cref="HealthStatus"/> (Healthy/Degraded → 200, Unhealthy → 503); this writer only shapes
/// the body, so a probe that looks at the status code alone is unaffected.
/// </summary>
public static class EShopHealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Writes the safe health payload. Assign to
    /// <c>HealthCheckOptions.ResponseWriter</c> on every mapped health endpoint.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";

        var payload = new HealthResponse
        {
            Status = report.Status.ToString(),
            TotalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            Checks = report.Entries
                .Select(entry => new HealthCheckResponse
                {
                    Name = entry.Key,
                    Status = entry.Value.Status.ToString()
                })
                .ToArray()
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private sealed record HealthResponse
    {
        public string Status { get; init; } = string.Empty;
        public double TotalDurationMs { get; init; }
        public HealthCheckResponse[] Checks { get; init; } = [];
    }

    private sealed record HealthCheckResponse
    {
        public string Name { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }
}
