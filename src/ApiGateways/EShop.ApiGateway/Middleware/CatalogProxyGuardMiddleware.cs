using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class CatalogProxyGuardMiddleware
{
    private static readonly string[] CatalogPathPrefixes =
    [
        "/api/v1/products",
        "/api/v1/categories"
    ];

    private readonly RequestDelegate _next;
    private readonly CatalogProxyOptions _options;

    public CatalogProxyGuardMiddleware(RequestDelegate next, IOptions<CatalogProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCatalogPath(context.Request.Path))
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
                message = "Request payload exceeds allowed size for catalog endpoints."
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

    private static bool IsCatalogPath(PathString path)
    {
        for (var i = 0; i < CatalogPathPrefixes.Length; i++)
        {
            if (path.StartsWithSegments(CatalogPathPrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
