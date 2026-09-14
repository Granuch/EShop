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

    // Endpoints that should have uniform timing.
    // confirm-email and refresh-token were missing: confirm-email reveals whether a UserId
    // exists (a hit does token validation work, a miss returns immediately), and refresh-token
    // reveals whether a presented token matched a row. Both are the same enumeration oracle the
    // other four are padded against.
    private static readonly string[] TimedEndpoints =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/register",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/confirm-email",
        "/api/v1/auth/refresh-token"
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

                    // Honour disconnection. Without the token this holds a request thread for up
                    // to ~1.2s per request after the client has already gone, which is both waste
                    // and a cheap way to pin resources. The padding exists to hide *response*
                    // timing from a client that is still listening; there is nothing to hide from
                    // one that has hung up.
                    try
                    {
                        await Task.Delay(delayMs, context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        // Client disconnected mid-delay. Swallowed deliberately: this runs in a
                        // finally block, and letting it propagate would replace the real response
                        // (or a real exception) with a cancellation.
                    }
                }

                // Note: If actual time exceeds minimum, we don't add delay
                // This is acceptable as it doesn't leak timing information
                // (could be slow password hashing, database query, etc.)
            }
        }
    }
}
