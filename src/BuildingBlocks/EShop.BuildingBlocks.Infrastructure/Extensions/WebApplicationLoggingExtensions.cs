using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>
/// Shared logging middleware configuration for HTTP APIs.
/// </summary>
public static class WebApplicationLoggingExtensions
{
    private static readonly string[] NoisePathPrefixes =
    [
        "/health",
        "/metrics",
        "/prometheus",
        "/openapi",
        "/scalar"
    ];

    /// <summary>
    /// Adds standardized Serilog request logging with noise suppression for technical endpoints.
    /// </summary>
    /// <param name="app">Current web application.</param>
    /// <returns>The same <see cref="WebApplication"/> instance for chaining.</returns>
    public static WebApplication UseEShopRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception != null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    return LogEventLevel.Error;
                }

                var path = httpContext.Request.Path.Value;
                if (IsNoisePath(path))
                {
                    return LogEventLevel.Verbose;
                }

                if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
                {
                    return LogEventLevel.Warning;
                }

                return LogEventLevel.Information;
            };

            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
                diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString());
                diagnosticContext.Set("TraceId", System.Diagnostics.Activity.Current?.TraceId.ToString());
                diagnosticContext.Set("SpanId", System.Diagnostics.Activity.Current?.SpanId.ToString());
            };
        });

        return app;
    }

    private static bool IsNoisePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var prefix in NoisePathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
