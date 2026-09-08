using EShop.ApiGateway.Configuration;
using EShop.BuildingBlocks.Infrastructure.Http;
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
            await EShopProblem.WriteAsync(context, EShopProblem.Create(
                context,
                StatusCodes.Status413PayloadTooLarge,
                detail: "Request payload exceeds allowed size for ordering endpoints.",
                errorCode: ProblemErrorCodes.PayloadTooLarge));
            return;
        }

        await _next(context);

        if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status502BadGateway)
        {
            // Set Retry-After before writing: EShopProblem.WriteAsync goes through
            // Results.Problem, which starts the response and takes the status from the
            // ProblemDetails, so any header added afterwards would be dropped.
            context.Response.Headers["Retry-After"] = Math.Max(1, _options.UpstreamUnavailableRetryAfterSeconds).ToString();
            await EShopProblem.WriteAsync(context, EShopProblem.Create(
                context,
                StatusCodes.Status503ServiceUnavailable,
                detail: "The ordering service is temporarily unavailable. Please retry.",
                errorCode: ProblemErrorCodes.UpstreamUnavailable));
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
        if (path.StartsWithSegments(OrderingPathPrefixes[0], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!path.StartsWithSegments(OrderingPathPrefixes[1], out var remainingPath))
        {
            return false;
        }

        var segments = remainingPath.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments is null || segments.Length < 2)
        {
            return false;
        }

        return string.Equals(segments[1], "orders", StringComparison.OrdinalIgnoreCase);
    }
}
