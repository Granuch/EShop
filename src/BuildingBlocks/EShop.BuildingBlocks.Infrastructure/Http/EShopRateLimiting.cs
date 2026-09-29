using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The one 429 every EShop component answers: problem+json with <c>errorCode</c> <c>Request.RateLimited</c>, plus
/// <c>Retry-After</c> when the limiter knows the window. <c>RejectionStatusCode</c> alone sets the status and writes
/// no body, so the gateway and five services used to answer an empty 429 while Identity alone sent this envelope
/// (frontend-contracts F-05).
/// </summary>
public static class EShopRateLimiting
{
    public const string RejectionDetail = "Too many requests. Please retry later.";

    public static RateLimiterOptions UseEShopRejectionResponse(this RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = WriteRejectionAsync;
        return options;
    }

    public static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // Rounded up: a fixed window reports the time left to its end, and truncating 0.4 s would advertise
        // "Retry-After: 0", an instruction to retry at once into the same closed window.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await EShopProblem.WriteAsync(context.HttpContext, EShopProblem.Create(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            detail: RejectionDetail,
            errorCode: ProblemErrorCodes.RateLimited));
    }
}
