using EShop.Identity.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EShop.Identity.API.Infrastructure.Middleware;

/// <summary>
/// Middleware that enforces uniform response timing for authentication endpoints.
/// 
/// Security rationale:
/// - Prevents account enumeration through timing attacks
/// - Valid and invalid login attempts take the same time
/// - Makes it harder to distinguish between "user exists" vs "wrong password"
/// 
/// Implementation:
/// - Measures actual processing time
/// - Adds delay to reach minimum response time
/// - Adds random jitter to prevent pattern detection
/// - Only applies to authentication endpoints (/api/v1/auth/*)
/// 
/// Trade-offs:
/// - Adds artificial delay to successful logins (acceptable for security)
/// - Slightly increased server resource usage (minimal)
/// - Better security posture outweighs performance impact
/// </summary>
public class UniformResponseTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BruteForceProtectionSettings _settings;

    // Endpoints that should have uniform timing
    private static readonly string[] TimedEndpoints =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/register",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password"
    ];

    public UniformResponseTimingMiddleware(
        RequestDelegate next,
        IOptions<BruteForceProtectionSettings> settings)
    {
        _next = next;
        _settings = settings.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is an endpoint that needs uniform timing
        var requiresUniformTiming = TimedEndpoints.Any(endpoint =>
            context.Request.Path.StartsWithSegments(endpoint, StringComparison.OrdinalIgnoreCase));

        if (!requiresUniformTiming)
        {
            await _next(context);
            return;
        }

        // Start timing
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Process the request
            await _next(context);
        }
        finally
        {
            if (_settings.MinimumResponseTimeMs > 0)
            {
                stopwatch.Stop();

                // Calculate how long to wait to reach minimum response time
                // Add random jitter to prevent timing pattern detection
                var jitter = Random.Shared.Next(0, _settings.ResponseTimeVariationMs);
                var targetResponseTime = _settings.MinimumResponseTimeMs + jitter;
                var actualResponseTime = (int)stopwatch.ElapsedMilliseconds;

                if (actualResponseTime < targetResponseTime)
                {
                    var delayMs = targetResponseTime - actualResponseTime;
                    await Task.Delay(delayMs);
                }

                // Note: If actual time exceeds minimum, we don't add delay
                // This is acceptable as it doesn't leak timing information
                // (could be slow password hashing, database query, etc.)
            }
        }
    }
}
