using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class OrderingProxyGuardMiddleware
{
    private static readonly string[] OrderingPathPrefixes =
    [
        "/api/v1/orders",
        "/api/v1/users"
    ];

    private readonly RequestDelegate _next;
    private readonly OrderingProxyOptions _options;

    public OrderingProxyGuardMiddleware(RequestDelegate next, IOptions<OrderingProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsOrderingPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (IsPayloadTooLarge(context.Request.ContentLength))
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Request.PayloadTooLarge",
                message = "Request payload exceeds allowed size for ordering endpoints."
            });
            return;
        }

        await _next(context);

        if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status502BadGateway)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = Math.Max(1, _options.UpstreamUnavailableRetryAfterSeconds).ToString();
        }
    }

    private bool IsPayloadTooLarge(long? contentLength)
    {
        return _options.MaxRequestBodySizeBytes > 0
            && contentLength.HasValue
            && contentLength.Value > _options.MaxRequestBodySizeBytes;
    }

    private static bool IsOrderingPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith(OrderingPathPrefixes[0], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.StartsWith(OrderingPathPrefixes[1], StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("/orders", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
