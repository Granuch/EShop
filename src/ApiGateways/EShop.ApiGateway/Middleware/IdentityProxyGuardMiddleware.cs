using EShop.ApiGateway.Configuration;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class IdentityProxyGuardMiddleware
{
    private static readonly string[] IdentityPathPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/account",
        "/api/v1/roles"
    ];

    private readonly RequestDelegate _next;
    private readonly IdentityProxyOptions _options;

    public IdentityProxyGuardMiddleware(RequestDelegate next, IOptions<IdentityProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIdentityPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (IsPayloadTooLarge(context.Request.ContentLength))
        {
            await EShopProblem.WriteAsync(context, EShopProblem.Create(
                context,
                StatusCodes.Status413PayloadTooLarge,
                detail: "Request payload exceeds allowed size for identity endpoints.",
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
                detail: "The identity service is temporarily unavailable. Please retry.",
                errorCode: ProblemErrorCodes.UpstreamUnavailable));
        }
    }

    private bool IsPayloadTooLarge(long? contentLength)
    {
        return _options.MaxRequestBodySizeBytes > 0
            && contentLength.HasValue
            && contentLength.Value > _options.MaxRequestBodySizeBytes;
    }

    private static bool IsIdentityPath(PathString path)
    {
        for (var i = 0; i < IdentityPathPrefixes.Length; i++)
        {
            if (path.StartsWithSegments(IdentityPathPrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
