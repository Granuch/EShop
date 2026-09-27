using EShop.ApiGateway.Configuration;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

/// <summary>
/// G6 (Admin panel S12). The fifth proxy guard, added with the notification route rather than after it: a route on a
/// path no guard covers works perfectly while silently losing the request-body cap and the 502-to-503 rewrite, which is
/// exactly the hole <c>ProxyGuardCoverageTests</c> exists to refuse.
/// </summary>
public sealed class NotificationProxyGuardMiddleware
{
    /// <inheritdoc cref="IdentityProxyGuardMiddleware.IdentityPathPrefixes"/>
    public const string NotificationPathPrefix = "/api/v1/notifications";

    private readonly RequestDelegate _next;
    private readonly NotificationProxyOptions _options;

    public NotificationProxyGuardMiddleware(RequestDelegate next, IOptions<NotificationProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsNotificationPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (IsPayloadTooLarge(context.Request.ContentLength))
        {
            await EShopProblem.WriteAsync(context, EShopProblem.Create(
                context,
                StatusCodes.Status413PayloadTooLarge,
                detail: "Request payload exceeds allowed size for notification endpoints.",
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
                detail: "The notification service is temporarily unavailable. Please retry.",
                errorCode: ProblemErrorCodes.UpstreamUnavailable));
        }
    }

    private bool IsPayloadTooLarge(long? contentLength)
    {
        return _options.MaxRequestBodySizeBytes > 0
            && contentLength.HasValue
            && contentLength.Value > _options.MaxRequestBodySizeBytes;
    }

    private static bool IsNotificationPath(PathString path)
    {
        // StartsWithSegments, not StartsWith: "/api/v1/notificationsomething" is a different resource.
        return path.StartsWithSegments(NotificationPathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
