using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The response headers a browser client on another origin may read. CORS hides every response header outside the
/// CORS-safelisted set unless the policy exposes it, so without this a cross-origin client could not read the
/// <c>Location</c> of a 201/202, the <c>Retry-After</c> of a 429/503, the correlation id, or the CSV export's filename
/// (frontend-contracts F-04).
/// </summary>
public static class EShopCors
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    public static readonly IReadOnlyList<string> ExposedHeaders =
    [
        HeaderNames.Location,
        HeaderNames.RetryAfter,
        CorrelationIdHeader,
        HeaderNames.ContentDisposition,
    ];

    public static CorsPolicyBuilder WithEShopExposedHeaders(this CorsPolicyBuilder policy)
        => policy.WithExposedHeaders([.. ExposedHeaders]);
}
