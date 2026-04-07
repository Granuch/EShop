using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class BasketProxyGuardMiddleware
{
    private const string BasketPathPrefix = "/api/v1/basket";

    private readonly RequestDelegate _next;
    private readonly BasketProxyOptions _options;

    public BasketProxyGuardMiddleware(RequestDelegate next, IOptions<BasketProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsBasketPath(context.Request.Path))
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
                message = "Request payload exceeds allowed size for basket endpoints."
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

    private static bool IsBasketPath(PathString path)
    {
        var value = path.Value;
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith(BasketPathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
