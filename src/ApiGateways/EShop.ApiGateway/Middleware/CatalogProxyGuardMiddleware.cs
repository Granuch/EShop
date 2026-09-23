using EShop.ApiGateway.Configuration;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class CatalogProxyGuardMiddleware
{
    /// <inheritdoc cref="IdentityProxyGuardMiddleware.IdentityPathPrefixes"/>
    public static readonly string[] CatalogPathPrefixes =
    [
        "/api/v1/products",
        "/api/v1/categories",
        // G6 (Admin panel S4). Must be added alongside any new /api/v1/admin/catalog route: this
        // array is what applies the request-body cap and turns a bare 502 into a ProblemDetails
        // body, and it is matched by prefix only — a route the gateway proxies but this list does
        // not name is guarded by nothing, silently. ProxyGuardCoverageTests is what catches it.
        "/api/v1/admin/catalog"
    ];

    /// <summary>
    /// G8 (admin panel S16). Catalog paths whose body is capped by
    /// <see cref="CatalogProxyOptions.ImportMaxRequestBodySizeBytes"/> instead of the general cap: product import, whose
    /// largest legal request is several megabytes. Bulk actions are <b>not</b> here — a thousand ids or prices is well
    /// under the general megabyte — so they keep the tighter cap.
    /// </summary>
    public static readonly string[] LargeBodyPathPrefixes =
    [
        "/api/v1/products/import"
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

        if (IsPayloadTooLarge(context.Request.Path, context.Request.ContentLength))
        {
            await EShopProblem.WriteAsync(context, EShopProblem.Create(
                context,
                StatusCodes.Status413PayloadTooLarge,
                detail: "Request payload exceeds allowed size for catalog endpoints.",
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
                detail: "The catalog service is temporarily unavailable. Please retry.",
                errorCode: ProblemErrorCodes.UpstreamUnavailable));
        }
    }

    private bool IsPayloadTooLarge(PathString path, long? contentLength)
    {
        var limit = MatchesAny(path, LargeBodyPathPrefixes)
            ? _options.ImportMaxRequestBodySizeBytes
            : _options.MaxRequestBodySizeBytes;

        return limit > 0
            && contentLength.HasValue
            && contentLength.Value > limit;
    }

    private static bool IsCatalogPath(PathString path) => MatchesAny(path, CatalogPathPrefixes);

    private static bool MatchesAny(PathString path, string[] prefixes)
    {
        for (var i = 0; i < prefixes.Length; i++)
        {
            if (path.StartsWithSegments(prefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
